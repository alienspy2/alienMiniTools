# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

GeminiCall은 Google GenAI (Gemini) API를 래핑하여 Ollama 호환 API와 JSON-RPC 2.0 API를 제공하는 FastAPI 기반 경량 서버입니다.

## 개발 명령어

```bash
# 서버 실행 (uv 사용)
uv run python main.py --verbose

# 서버 실행 (배치 파일)
run_server.bat

# 단위 테스트 실행
uv run pytest tests/

# 특정 테스트 파일 실행
uv run pytest tests/test_api.py

# E2E 테스트 (서버 실행 중 필요)
uv run python test_fake_ollama.py     # Ollama API 테스트
uv run python test_jsonrpc.py         # JSON-RPC 채팅 클라이언트
uv run python test_gemini_tts.py      # TTS 테스트
```

## 설정

`config.json` 파일 필요 (예시: `config-example.json` 참고):
- `api_key`: Google AI API 키
- `models`: 사용 가능한 모델 목록 (첫 번째가 기본값)
- `rpm`: 분당 요청 제한
- `http_port`: 서버 포트

## 아키텍처

```
main.py (FastAPI 앱)
    ├── /health              - 서버 상태
    ├── /api/chat            - Ollama 호환 채팅
    ├── /api/generate        - Ollama 호환 생성
    ├── /api/tags            - 모델 목록
    └── /generate            - JSON-RPC 2.0 엔드포인트
            │
            ▼
    QueueManager (queue_manager.py)
        - 비동기 요청 큐 관리
        - 백그라운드 워커로 요청 처리
            │
            ├── RateLimiter (rate_limiter.py)
            │     - 슬라이딩 윈도우 방식 RPM 제한
            │
            └── GenAIService (genai_service.py)
                  - Google GenAI SDK 래퍼
                  - Ollama→Gemini 메시지 포맷 변환
                  - run_in_executor로 동기 API 비동기 처리
```

## 핵심 흐름

1. 요청이 들어오면 `QueueManager.submit_request()`로 큐에 추가
2. 백그라운드 워커가 `RateLimiter.wait_for_slot()`으로 속도 제한 확인
3. `GenAIService.generate_response()`가 Gemini API 호출
4. 결과가 Future를 통해 엔드포인트로 반환

## 환경 변수

- `GEMINICALL_VERBOSE`: 설정 시 DEBUG 레벨 로깅 활성화
