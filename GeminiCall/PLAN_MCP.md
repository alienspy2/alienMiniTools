# MCP 기능 추가 구현 계획

## Context

GeminiCall 서버에 MCP(Model Context Protocol) 서버 관리 및 ReAct 방식 에이전트 루프를 추가한다.
Gemma 3는 네이티브 Tool Calling이 없으므로, 프롬프트로 JSON tool call을 유도하고
서버 단에서 MCP 도구 호출 → 결과 주입 → 재호출을 반복하는 에이전트 루프를 구현한다.

## 핵심 워크플로우: ReAct 에이전트 루프

```
Client → POST /generate_with_mcp
           │
           ▼
    ┌─ MCP 서버들에 SSE 연결 → list_tools() → 도구 목록 수집
    │
    │  ┌─────────────── Agent Loop (max 5회) ──────────────┐
    │  │                                                    │
    │  │  1. [Thought & Action]                             │
    │  │     도구 목록 + 대화 내역 → Gemma 3 호출           │
    │  │                                                    │
    │  │  2. [Parsing]                                      │
    │  │     응답에서 JSON tool call 추출                    │
    │  │     ├─ JSON 없음 → 최종 답변, 루프 종료            │
    │  │     └─ JSON 있음 ↓                                 │
    │  │                                                    │
    │  │  3. [Observation]                                  │
    │  │     MCP call_tool() 실행                           │
    │  │     결과를 대화 내역에 추가                         │
    │  │     → 1번으로 회귀                                 │
    │  │                                                    │
    │  └────────────────────────────────────────────────────┘
    │
    └─ 최종 답변을 JSON-RPC 응답으로 반환
```

**기존 plan과의 차이점:**
- ~~1차 호출 → tool call → 2차 호출로 끝~~ → **최대 N회 반복하는 ReAct 루프**
- 매 라운드마다 **전체 대화 내역**(system + user + assistant + observation)을 누적하여 전달
- Format Fixer: Gemma 3의 JSON 출력 오류 보정 로직 추가
- MCP/LLM 호출에 타임아웃 설정

## 변경 파일 요약

| 파일 | 작업 | 설명 |
|---|---|---|
| `mcp_service.py` | 신규 | McpServerRegistry + McpToolService (에이전트 루프 포함) |
| `schema.py` | 수정 | MCP 관련 Pydantic 모델 추가 (기존 코드 변경 없음) |
| `main.py` | 수정 | lifespan에 MCP 초기화, 엔드포인트 4개 추가 |
| `pyproject.toml` | 수정 | `mcp>=1.0.0` 의존성 추가 |
| `tests/test_mcp_service.py` | 신규 | 단위 테스트 |
| `tests/test_mcp_api.py` | 신규 | 통합 테스트 |

---

## 구현 순서

### 1단계: 의존성 추가 (`pyproject.toml`)

- `mcp>=1.0.0` 추가 후 `uv sync`

### 2단계: MCP 서비스 (`mcp_service.py` 신규)

#### McpServerRegistry — MCP 서버 목록 관리 (메모리 + `mcp.json` 영속화)

```python
class McpServerRegistry:
    __init__(config_dir)  # config_dir 내 mcp.json 경로 결정, 파일 있으면 자동 로드
    add(name, url)        # 등록 (이미 존재하면 URL 업데이트) → mcp.json 저장
    remove(name) -> bool  # 제거 → mcp.json 저장
    list_all() -> list    # 전체 목록
    get(name) -> dict     # 이름으로 조회
    get_all_urls() -> dict[str, str]  # {name: url}
    _save()               # 내부: self._servers → mcp.json에 쓰기
    _load()               # 내부: mcp.json → self._servers에 읽기
```

**`mcp.json` 파일 형식:**

```json
[
  {"name": "tts", "url": "http://192.168.0.18:23016/sse"},
  {"name": "search", "url": "http://localhost:8080/sse"}
]
```

- `/mcp/add`, `/mcp/remove` 호출 시마다 `mcp.json` 자동 저장
- 서버 시작 시 `mcp.json` 존재하면 자동 로드, 없으면 빈 배열(`[]`)로 파일 생성
- `mcp.json`은 `config.json`과 같은 디렉토리에 위치

#### McpToolService — 도구 수집/호출/파싱 + 에이전트 루프

**프롬프트 설계 (ReAct 방식):**

```
SYSTEM_PROMPT:
  You are a helpful assistant with access to the following tools:
  {도구 목록 JSON}

  When you need to use a tool, respond with ONLY:
  ```json
  {"tool": "서버명.도구명", "arguments": {...}}
  ```
  If no tool is needed, respond normally in plain text.
```

**에이전트 루프 (핵심 메서드):**

```python
async def run_agent_loop(
    self,
    qm: QueueManager,          # Gemini API 호출용 (RPM 제한 적용)
    server_urls: dict[str, str],  # MCP 서버 {name: url}
    model: str,
    messages: list[dict],       # 사용자 대화 내역
    options: dict,
    max_iterations: int = 5,    # 최대 루프 횟수
    tool_timeout: float = 30.0, # MCP 호출 타임아웃 (초)
) -> str:
    """
    1. MCP 서버들에서 도구 목록 수집 (collect_tools)
    2. system prompt에 도구 설명 삽입
    3. 루프 시작:
       a. QueueManager 경유 LLM 호출 (대화 내역 전체 전달)
       b. 응답에서 tool call JSON 파싱
          - 없으면: 최종 답변 반환
          - 있으면: MCP call_tool 실행
       c. assistant 응답 + tool 결과(observation)를 대화 내역에 추가
       d. 다음 라운드로
    4. max_iterations 도달 시 마지막 응답 반환
    """
```

**대화 내역 누적 형태:**

```python
messages = [
    {"role": "system",    "content": "도구 목록 포함 시스템 프롬프트"},
    {"role": "user",      "content": "안녕하세요를 음성으로 생성해줘"},
    # --- 라운드 1 ---
    {"role": "assistant", "content": '{"tool": "tts.generate_speech", "arguments": {"text": "안녕하세요"}}'},
    {"role": "user",      "content": "[Tool Result: tts.generate_speech]\nSuccess: 음성 생성 완료..."},
    # --- 라운드 2 ---
    {"role": "assistant", "content": "음성 생성이 완료되었습니다!"},  # → 최종 답변
]
```

**기타 메서드:**

- `collect_tools_from_servers(server_urls)` — 여러 MCP 서버에 병렬 SSE 연결 → `list_tools()` 수집
- `call_mcp_tool(server_url, tool_name, args, timeout)` — SSE 연결 → `call_tool()` 호출 (타임아웃 적용)
- `build_tools_description(tools_by_server)` — 도구 → 프롬프트 텍스트, `서버명.도구명` 형태
- `extract_tool_call(text)` — 응답에서 JSON 추출 + **Format Fixer** (불완전 JSON 보정)
- `parse_qualified_tool_name(name)` — `서버명.도구명` → `(server, tool)` 파싱
- `_resolve_server_url(...)` — 도구 이름에서 MCP 서버 URL 찾기

**Format Fixer (예방 조치):**

```python
def extract_tool_call(text: str) -> dict | None:
    # 1차: ```json ... ``` 코드블록에서 추출
    # 2차: 코드블록 없이 bare JSON 추출
    # 3차: 작은따옴표→큰따옴표, trailing comma 제거 등 보정 후 재시도
```

### 3단계: 스키마 추가 (`schema.py` 하단에 추가)

```python
# MCP 서버 관리
McpServerAddRequest      (name: str, url: str)
McpServerAddResponse     (name, url, message="registered")
McpServerRemoveRequest   (name: str)
McpServerRemoveResponse  (name, message="removed")
McpServerInfo            (name, url)
McpServerListResponse    (servers: List[McpServerInfo])

# /generate_with_mcp 전용
McpRPCParams(BaseModel):
    model: Optional[str]
    messages: List[Dict[str, Any]]
    temperature: Optional[float]
    max_output_tokens: Optional[int]
    mcp_servers: Optional[List[str]]  # 특정 서버만 사용 (None=전체)
    max_iterations: Optional[int] = 5  # 에이전트 루프 최대 횟수

McpRPCRequest(BaseModel):
    jsonrpc: str = "2.0"
    method: str
    params: McpRPCParams
    id: Union[int, str, None]
```

### 4단계: 엔드포인트 추가 (`main.py`)

**lifespan 수정:**

```python
# 기존 코드 유지 + 추가:
# McpServerRegistry 생성 시 mcp.json 자동 로드
app.state.mcp_registry = McpServerRegistry(config_dir=script_dir)
app.state.mcp_tool_service = McpToolService()
logger.info(f"Loaded {len(app.state.mcp_registry.list_all())} MCP servers from mcp.json")
```

**MCP 서버 관리 엔드포인트 3개:**

```
POST   /mcp/add     → 서버 등록
DELETE /mcp/remove   → 서버 제거 (없으면 404)
GET    /mcp/list     → 서버 목록 반환
```

**생성 엔드포인트:**

```python
@app.post("/generate_with_mcp", response_model=RPCResponse)
async def generate_with_mcp(request: McpRPCRequest):
    # 1. JSON-RPC 2.0 검증
    # 2. 사용할 MCP 서버 URL 결정 (params.mcp_servers 또는 전체)
    # 3. MCP 서버 없으면 일반 /generate로 폴백
    # 4. mcp_tool_service.run_agent_loop() 호출
    #    - QueueManager 경유 (RPM 제한 적용)
    #    - max_iterations 전달
    # 5. RPCResponse로 반환
```

**헬퍼 함수:**

- `_adapt_rpc_messages()` — RPC 메시지 → 내부 포맷 (기존 /generate 로직 추출)
- `_fallback_generate()` — MCP 없을 때 일반 generate 처리

### 5단계: 테스트 작성

**`tests/test_mcp_service.py`:**
- McpServerRegistry: add/remove/list/get CRUD
- extract_tool_call: 코드블록 파싱, bare JSON 파싱, format fixer, 매칭 실패
- parse_qualified_tool_name: `서버.도구`, `도구만`

**`tests/test_mcp_api.py`:**
- POST /mcp/add, DELETE /mcp/remove, GET /mcp/list
- /generate_with_mcp: MCP 서버 없을 때 폴백 동작

---

## 설계 결정

| 결정 | 근거 |
|---|---|
| ReAct 에이전트 루프 (max N회) | 1차-2차로는 복잡한 작업 불가. 도구 결과 보고 추가 도구 호출 가능해야 함 |
| 대화 내역 누적 방식 | followup prompt 방식은 이전 맥락 소실. 전체 내역 누적이 정확도 높음 |
| MCP 연결: 요청마다 연결/해제 | 상시 연결보다 단순, MCP 서버 장애 시 복구 쉬움 |
| QueueManager 경유 LLM 호출 | RPM 제한 유지. MCP 오케스트레이션만 엔드포인트 레벨에서 수행 |
| `mcp_service.py` 별도 분리 | `genai_service.py`는 순수 GenAI 래퍼 유지 |
| `서버명.도구명` qualified name | 여러 MCP 서버의 도구 이름 충돌 방지 |
| Format Fixer | Gemma 3가 JSON을 소폭 어길 경우 대비 |
| 타임아웃 (기본 30초) | MCP 서버/LLM 무응답 방지 |
| `mcp.json` 영속화 | add/remove 시 즉시 저장, 서버 재시작 시 자동 복원 |

---

## 검증 방법

```bash
# 1. 단위/통합 테스트
uv run pytest tests/test_mcp_service.py tests/test_mcp_api.py -v

# 2. 전체 테스트 스위트
uv run pytest tests/ -v

# 3. E2E 테스트 (서버 실행 후)
# MCP 서버 등록
curl -X POST http://localhost:20006/mcp/add \
  -H "Content-Type: application/json" \
  -d '{"name": "tts", "url": "http://192.168.0.18:23016/sse"}'

# 목록 확인
curl http://localhost:20006/mcp/list

# MCP 도구 활용 생성 (에이전트 루프)
curl -X POST http://localhost:20006/generate_with_mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "generate_content",
    "params": {
      "messages": [{"role": "user", "content": "안녕하세요를 음성으로 생성해줘"}],
      "max_iterations": 5
    },
    "id": 1
  }'
```
