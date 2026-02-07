# Gemini API Server Implementation Plan (Revised)

## 1. 개요
FastAPI를 기반으로 작동하는 Python 애플리케이션입니다. Google의 **Gen AI SDK** (`google-genai`)를 사용하여 Gemma/Gemini 모델을 호출하고 텍스트 생성 기능을 제공하며, 클라이언트와의 통신은 **JSON-RPC 2.0** 프로토콜을 따릅니다.

## 2. 설정 파일 명세 (`config.json`)
애플리케이션 구동에 필요한 설정은 `config.json` 파일에서 관리합니다.

| Key | Type | 설명 | 예시 값 |
| :--- | :--- | :--- | :--- |
| `api_key` | String | Google AI Studio API Key | `"AIzaSy..."` |
| `models` | List[String] | 사용할 모델 목록 | `["gemini-3-flash-preview", "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemma-2-27b-it", "gemma-2-9b-it", "gemma-2-2b-it"]` |
| `rpm` | Integer | 분당 요청 허용 수 (Rate Limit) | `15` |
| `http_port` | Integer | 서버 실행 포트 | `20006` |

## 3. API 명세
두 가지 프로토콜을 모두 지원합니다.
1. **Ollama 호환 REST API**: `/api/chat` (기존 도구 호환용)
2. **JSON-RPC 2.0**: `/generate` (내부 및 커스텀 클라이언트용)

### 3.1. Ollama 호환 REST API
#### POST `/api/chat`
*(Ollama Chat Request/Response 형식 준수. 앞서 정의한 내용과 동일)*

#### GET `/api/tags` (Model List)
*(상동)*

#### GET `/api/version` (Ollama Version)
서버 버전을 반환합니다 (Ollama 클라이언트 호환성용).
**Response**: `{"version": "0.1.0"}`

#### GET `/health` (Health Check)
서버 및 Gen AI 서비스 상태를 확인합니다.
**Response**: 
```json
{
  "status": "ok", 
  "queue_size": 0, 
  "rpm_usage": "2/15"
}
```

### 3.2. JSON-RPC 2.0 (`POST /generate`)
**Request**:
```json
{
  "jsonrpc": "2.0",
  "method": "generate_content",
  "params": {
    "model": "gemma-2-27b-it",
    "messages": [
       {"role": "user", "parts": ["안녕"]},
       {"role": "model", "parts": ["반갑습니다"]}
    ],
    "temperature": 0.7
  },
  "id": 1
}
```
**Response**:
```json
{
  "jsonrpc": "2.0",
  "result": {
    "generated_text": "응답 내용...",
    "finish_reason": "STOP"
  },
  "id": 1
}
```

### 3.3. 공통 사항
- **Port**: `config.json`의 `http_port` 사용 (기본 20006)
- **Queue**: 두 엔드포인트 모두 동일한 `Queue Manager`를 공유하여 RPM 제한을 함께 적용받음.

## 4. 아키텍처 및 구현 계획

### 4.1. 프로젝트 구조
```
GeminiCall/
├── config.json          # 설정 파일 
├── config_loader.py     # 설정 로딩 및 유효성 검사
├── schema.py            # JSON-RPC 요청/응답 Pydantic 모델
├── genai_service.py     # Google Gen AI SDK 호출 로직 (비동기 처리)
├── rate_limiter.py      # RPM 제한 기능
├── queue_manager.py     # 요청 대기열(Queue) 관리
├── main.py              # FastAPI 앱 및 JSON-RPC 라우터
└── tests/               # Pytest 테스트 코드
    ├── test_config.py
    ├── test_rpc_schema.py
    ├── test_rate_limiter.py
    ├── test_queue.py
    └── test_integration.py
```

### 4.2. 구현 단계 (Phases)

#### Phase 1: 기본 환경 및 설정 모듈
- `config.json` 구조 정의 (API Key 방식)
- `config_loader.py`: UTF-8 with BOM 인코딩 지원
- `requirements.txt`: `fastapi`, `uvicorn`, `google-genai` 포함

#### Phase 2: 코어 로직 (Gen AI & Rate Limit)
- `rate_limiter.py`: RPM 제어 로직
- `genai_service.py`: `google-genai` 라이브러리 초기화 및 모델 호출
- 각 모듈 단위 테스트 작성

#### Phase 3: 큐잉 시스템 (Queue Manager)
- `queue_manager.py`: 요청을 큐(Queue)에 담고, 백그라운드 워커가 Rate Limit에 맞춰 순차 처리
- `asyncio.Queue` 활용하여 비동기 대기열 구현
- 큐 포화 시 처리 정책(선택사항) 고려

#### Phase 4: JSON-RPC 서버 구현
- `schema.py`: Pydantic을 이용한 JSON-RPC 모델링
- `main.py`: `/generate` 엔드포인트 구현, Queue Manager 통합
- 클라이언트 요청 시 큐에 넣고 결과(`Future`)를 기다려 반환하는 구조
- 통합 테스트 작성

## 5. 제약 사항 및 규칙 확인
1.  **파일 인코딩**: `.py` 파일은 반드시 **UTF-8 with BOM**으로 저장합니다.
2.  **언어**: 주석 및 로그 메시지는 한글을 지향합니다.
3.  **OS**: Windows 환경을 기준으로 경로 및 실행 스크립트를 작성합니다.

## 6. 구현 진행 상황
- **Phase 1: 기본 환경 및 설정 모듈**
    - [ ] `config.json` 작성 (모델 목록, API Key, RPM, Port 등)
    - [ ] `requirements.txt` 작성 (`google-genai` 포함)
    - [ ] `config_loader.py` 구현 (UTF-8 BOM 지원)
    - [ ] **Test**: `test_config.py` 실행 (설정 로딩 및 인코딩 테스트)

- **Phase 2: 코어 로직 (Gen AI & Rate Limit)**
    - [ ] `rate_limiter.py` 구현 (RPM 제한 알고리즘)
    - [ ] `genai_service.py` 구현 (`google-genai` 클라이언트 래퍼)
    - [ ] **Test**: `test_rate_limiter.py`, `test_genai_service.py` 실행 (단위 테스트)

- **Phase 3: 큐잉 시스템 (Queue Manager)**
    - [ ] `queue_manager.py` 구현 (요청 대기열 및 백그라운드 워커)
    - [ ] Rate Limiter 및 Gen AI Service와 연동
    - [ ] **Test**: `test_queue.py` 실행 (대기열 처리 및 속도 제한 검증)

- **Phase 4: API 서버 구현 (Ollama & JSON-RPC)**
    - [ ] `schema.py` 구현 (Ollama/JSON-RPC Request/Response 모델)
    - [ ] `main.py` 구현 (FastAPI 앱, 라우터, Lifespan)
    - [ ] 엔드포인트 구현: `/api/chat`, `/api/tags`, `/generate`
    - [ ] **Test**: `test_api_endpoints.py`, `test_integration.py` 실행 (통합 테스트)