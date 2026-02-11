# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

GeminiCall은 Google GenAI (Gemini) 및 OpenAI API를 래핑하여 Ollama 호환 API, JSON-RPC 2.0 API, MCP(Model Context Protocol) 연동을 제공하는 FastAPI 기반 경량 서버입니다.

## 개발 명령어

```bash
# 서버 실행 (uv 사용)
uv run python main.py --verbose

# 서버 실행 (배치 파일 / 셸 스크립트)
run_server.bat          # Windows
./run_server.sh         # Linux/macOS

# 단위 테스트 실행
uv run pytest tests/

# 특정 테스트 파일 실행
uv run pytest tests/test_api.py
uv run pytest tests/test_config.py
uv run pytest tests/test_queue.py
uv run pytest tests/test_genai_service.py
uv run pytest tests/test_rate_limiter.py
uv run pytest tests/test_mcp_api.py
uv run pytest tests/test_mcp_service.py

# E2E 테스트 (서버 실행 중 필요)
uv run python test_fake_ollama.py     # Ollama API 대화형 클라이언트
uv run python test_jsonrpc.py         # JSON-RPC 채팅 클라이언트
uv run python test_mcp_chat.py        # MCP 대화형 클라이언트 (/add, /remove, /list, /iter)
uv run python test_gemini_tts.py      # TTS 테스트
uv run python quick_test.py           # 단일 요청 테스트

# 배포
deploy.bat [instance-name] [port]     # 예: deploy.bat api-8000 8000
```

## 설정

`config.json` 파일 필요 (예시: `config-example.json` 참고).

### 레거시 형식 (단일 프로바이더)

```json
{
  "api_key": "YOUR_GEMINI_API_KEY",
  "models": ["gemini-2.5-flash", "gemma-3-27b-it"],
  "rpm": 15,
  "http_port": 20006
}
```

### 프로바이더 형식 (멀티 프로바이더, 권장)

```json
{
  "providers": {
    "gemini": {
      "api_key": "YOUR_GEMINI_API_KEY",
      "models": ["gemini-2.5-flash", "gemma-3-27b-it"],
      "rpm": 15
    },
    "openai": {
      "api_key": "YOUR_OPENAI_API_KEY",
      "models": ["gpt-4o", "gpt-4o-mini"],
      "rpm": 60
    }
  },
  "http_port": 20006
}
```

### 설정 키 설명

- `providers` (dict): 프로바이더별 설정 (gemini, openai 등)
  - `api_key` (string): API 키
  - `models` (list): 사용 가능한 모델 목록
  - `rpm` (int): 분당 요청 제한
- `http_port` (int): 서버 포트 (기본값: 20006)
- config_loader가 자동 생성하는 키: `all_models`, `model_provider_map`, 하위 호환용 `api_key`/`models`/`rpm`

## 파일 구조

```
main.py              - FastAPI 앱, 모든 엔드포인트, lifespan 관리
config_loader.py     - 설정 파일 로드 (레거시/프로바이더 형식 자동 변환)
queue_manager.py     - 비동기 요청 큐, 백그라운드 워커, 프로바이더 라우팅
rate_limiter.py      - 슬라이딩 윈도우 RPM 제한 (asyncio.Lock)
llm_service.py       - LLMService 추상 클래스 (ABC)
genai_service.py     - GenAIService(LLMService) - Gemini API 래퍼
openai_service.py    - OpenAIService(LLMService) - OpenAI API 래퍼
mcp_service.py       - McpServerRegistry (서버 등록/삭제), McpToolService (ReAct 에이전트 루프)
schema.py            - Pydantic 모델 (Ollama, JSON-RPC, MCP 요청/응답)
config-example.json  - 설정 파일 예시
mcp.json             - MCP 서버 레지스트리 (런타임에 관리)
run_server.bat       - Windows 실행 스크립트
run_server.sh        - Linux/macOS 실행 스크립트
deploy.bat           - Windows 배포 스크립트 (인스턴스 생성)
```

## 아키텍처

```
main.py (FastAPI 앱, uvicorn)
    │
    ├── /                    - 루트 ("Ollama is running")
    ├── /health              - 서버 상태 (큐 크기, RPM, 프로바이더 정보)
    ├── /api/version         - API 버전
    ├── /api/tags            - 모델 목록 (GET)
    ├── /api/show            - 모델 상세 정보 (POST)
    ├── /api/ps              - 실행 중 모델 목록 (GET)
    ├── /api/chat            - Ollama 호환 채팅 (POST, 메시지 히스토리)
    ├── /api/generate        - Ollama 호환 생성 (POST, 단일 프롬프트)
    ├── /generate            - JSON-RPC 2.0 엔드포인트 (POST)
    ├── /mcp/add             - MCP 서버 등록 (POST)
    ├── /mcp/remove          - MCP 서버 삭제 (DELETE)
    ├── /mcp/list            - MCP 서버 목록 (GET)
    └── /generate_with_mcp   - MCP ReAct 에이전트 생성 (POST)
            │
            ▼
    QueueManager (queue_manager.py)
        - asyncio.Queue 기반 비동기 요청 큐
        - 백그라운드 워커로 순차 처리
        - 모델명→프로바이더 자동 라우팅
            │
            ├── RateLimiter (rate_limiter.py) [프로바이더별 인스턴스]
            │     - 슬라이딩 윈도우 방식 RPM 제한
            │     - asyncio.Lock으로 동시성 제어
            │     - 60초 윈도우, 50ms 버퍼
            │
            ├── GenAIService (genai_service.py) [LLMService]
            │     - Google GenAI SDK 래퍼
            │     - Ollama→Gemini 메시지 포맷 변환
            │     - system 메시지 → system_instruction 분리
            │     - gemma-* 모델: system instruction을 첫 user 메시지에 합침
            │     - run_in_executor로 동기 API 비동기 처리
            │
            └── OpenAIService (openai_service.py) [LLMService]
                  - OpenAI SDK 래퍼
                  - Ollama↔OpenAI 포맷 거의 동일 (최소 변환)
                  - temperature, max_output_tokens→max_tokens 변환
                  - run_in_executor로 동기 API 비동기 처리
```

## 핵심 흐름

1. 요청이 들어오면 `QueueManager.submit_request(model, messages, options)` → asyncio.Future 반환
2. 백그라운드 워커가 모델명으로 프로바이더 결정 (`_resolve_provider`)
3. 해당 프로바이더의 `RateLimiter.wait_for_slot()`으로 속도 제한 확인
4. 해당 프로바이더의 `LLMService.generate_response()`가 API 호출
5. 결과가 Future를 통해 엔드포인트로 반환

### 프로바이더 라우팅 규칙

1. `config['model_provider_map']`에서 정확한 모델명 매칭
2. 모델명 접두사로 추론: `gpt-/o1-/o3-/o4-` → openai, `gemini-/gemma-` → gemini
3. 매칭 실패 시 첫 번째 프로바이더로 기본 라우팅

## MCP 연동

### McpServerRegistry

- `mcp.json`에 MCP 서버 목록 영구 저장
- REST API로 런타임에 서버 추가/삭제/조회

### McpToolService ReAct 에이전트 루프

1. 지정된 MCP 서버들에서 SSE로 도구 목록 수집
2. 시스템 프롬프트에 도구 설명 삽입
3. LLM 응답에서 도구 호출 JSON 추출 (`{"tool": "server.tool_name", "arguments": {...}}`)
4. 도구 실행 → 결과를 대화에 추가 → 반복 (최대 max_iterations)
5. 도구 호출 없으면 최종 답변으로 반환

## JSON-RPC 2.0

- 메서드: `generate_content`
- 요청: `{"jsonrpc": "2.0", "method": "generate_content", "params": {"model": "...", "messages": [...], "temperature": ..., "max_output_tokens": ...}, "id": ...}`
- 응답: `{"jsonrpc": "2.0", "result": {"generated_text": "...", "finish_reason": "STOP"}, "id": ...}`
- 에러 코드: `-32600` (잘못된 요청), `-32601` (메서드 없음), `-32603` (내부 에러)

## 메시지 포맷 변환

### Ollama → Gemini

- `system` 메시지 → `system_instruction` 파라미터로 분리
- `assistant` → `model` role로 변환
- `content` → `types.Part.from_text(text=...)` 래핑
- `gemma-*` 모델: system_instruction 미지원 → 첫 user 메시지에 `[System Instructions]` 접두사로 합침

### Ollama → OpenAI

- 거의 동일한 포맷 (role/content), 최소 변환
- `max_output_tokens` → `max_tokens`로 키 변환

## 환경 변수

- `GEMINICALL_VERBOSE`: 설정 시 DEBUG 레벨 로깅 활성화
- `--verbose` / `-v` CLI 플래그로도 활성화 가능 (GEMINICALL_VERBOSE=1 자동 설정)

## 주요 의존성

- `fastapi` >= 0.128.3 - 웹 프레임워크
- `uvicorn` >= 0.40.0 - ASGI 서버
- `google-genai` >= 1.62.0 - Google GenAI SDK
- `openai` >= 1.0.0 - OpenAI SDK
- `mcp` >= 1.0.0 - Model Context Protocol SDK
- `httpx` >= 0.28.1 - 비동기 HTTP 클라이언트
- `pytest` >= 9.0.2, `pytest-asyncio` >= 1.3.0 - 테스트
- Python >= 3.12

## 테스트 구조

### 단위 테스트 (tests/)

| 파일 | 대상 |
|------|------|
| test_api.py | API 엔드포인트 (health, tags, chat, generate, JSON-RPC) |
| test_config.py | 설정 로드 (레거시/프로바이더 형식, 유효성 검증) |
| test_queue.py | 큐 처리, 에러 전파 |
| test_genai_service.py | 메시지 변환, API 호출 |
| test_rate_limiter.py | RPM 제한 (허용/대기) |
| test_mcp_api.py | MCP 서버 관리, /generate_with_mcp |
| test_mcp_service.py | MCP 서비스 로직 |

### E2E 테스트 (루트 디렉토리, 서버 실행 필요)

| 파일 | 용도 |
|------|------|
| test_fake_ollama.py | Ollama API 대화형 클라이언트 |
| test_jsonrpc.py | JSON-RPC 대화형 클라이언트 (히스토리 유지) |
| test_mcp_chat.py | MCP 대화형 클라이언트 (--port, --model, --host, --max-iter) |
| test_gemini_tts.py | Gemini TTS → OGG/PCM 파일 저장 |
| quick_test.py | 단일 요청 테스트 |

## 에러 처리

- HTTP 엔드포인트: `HTTPException` (status_code + detail)
- JSON-RPC: 구조화된 에러 응답 (`code`, `message`)
- QueueManager: Future에 예외 설정, 워커는 로그 후 계속 처리
- MCP: 타임아웃 처리, `[ERROR]` 접두사로 도구 에러 전달

## 로깅

- 모듈별 로거: `GeminiCall`, `QueueManager`, `GenAIService`, `OpenAIService`, `McpService`
- `--verbose` 또는 `GEMINICALL_VERBOSE` 설정 시 DEBUG 레벨
