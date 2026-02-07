# QwenTTS 웹 챗 앱 + MCP 서버 구현 계획

## 🎯 프로젝트 개요

| 구성 요소 | 설명 |
|-----------|------|
| **Gradio TTS 서버** | `run.bat`으로 실행 중인 Qwen3-TTS (포트 23015) |
| **웹 챗 앱** | Gradio에 API로 연결하여 TTS 요청 → WAV 재생 |
| **MCP 서버** | JSON-RPC endpoint로 텍스트 → WAV 반환 |

---

## 📦 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                    QwenTTS Ecosystem                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────┐                                          │
│  │  run.bat         │  ← 이미 실행 중 (포트 23015)              │
│  │  Gradio TTS Demo │                                          │
│  └────────┬─────────┘                                          │
│           │ Gradio Client API                                  │
│           ▼                                                    │
│  ┌──────────────────────────────────────────────────────┐      │
│  │              tts_client.py (공통 클라이언트)            │      │
│  │                                                         │      │
│  │  ┌─────────────────┐    ┌──────────────────────┐    │      │
│  │  │ 웹 챗 UI        │    │ MCP Server           │    │      │
│  │  │ (web_ui.py)     │    │ (server.py)          │    │      │
│  │  │ 포트: 23007     │    │ 포트: 23016 (/mcp)   │    │      │
│  │  └─────────────────┘    └──────────────────────┘    │      │
│  └──────────────────────────────────────────────────────┘      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

## 3. 핵심 파일 구성

-   `Qwen3-TTS-apiwrap/`
    -   `pyproject.toml` : 의존성 관리 (`fastapi`, `fastmcp`, `gradio_client` 등)
    -   `tts_client.py` : Gradio Client 래퍼 (긴 텍스트 청킹, 오디오 정규화)
    -   `server.py` : **FastMCP 기반 MCP 서버**
        -   라이브러리: `fastmcp`
        -   프로토콜: `streamable-http` (n8n 호환)
        -   포트: 23016
    -   `web_ui.py` : **웹 챗 UI 서버**
        -   라이브러리: `FastAPI`
        -   포트: 23007
    -   `templates/index.html` : 채팅 UI HTML
    -   `static/style.css`, `app.js` : 스타일 및 로직
    -   `run.bat` (삭제됨/대체됨)
```

---

## 📝 단계별 구현 계획

### Phase 1: 핵심 TTS 클라이언트 (gradio_client)

```python
# 파일: tts_client.py
- Gradio Client를 사용하여 실행 중인 TTS 서버에 연결
- text → WAV 변환 함수 구현
- WAV 파일 저장 및 반환 기능
```

**주요 기능:**
- `gradio_client` 라이브러리 사용
- `run_instruct` 함수를 API로 호출 (text, language, speaker, instruct)
- numpy → WAV 파일 변환

---

### Phase 2: 웹 챗 UI (FastAPI + JavaScript)

```python
# 파일: app.py
- FastAPI 웹 서버
- 채팅 인터페이스 (HTML/CSS/JS)
- 텍스트 입력 → TTS → 브라우저에서 WAV 재생
```

**UI 기능:**
- 모던 다크 테마 채팅 인터페이스
- 텍스트 입력 박스
- 언어/스피커/감정 선택 드롭다운
- 생성된 오디오 자동 재생
- 채팅 히스토리 표시

---

### Phase 3: MCP 서버 (JSON-RPC)

```python
# 파일: app.py (MCP 통합)
- JSON-RPC 2.0 endpoint (/rpc)
- 메서드: tts.generate(text, language, speaker, instruct)
- 반환: base64 인코딩된 WAV
```

**MCP 스펙:** (기본값: speaker=Sohee, language=Korean)
```json
{
  "jsonrpc": "2.0",
  "method": "tts.generate",
  "params": {
    "text": "안녕하세요, 반갑습니다!",
    "language": "Korean",
    "speaker": "Sohee"
  },
  "id": 1
}
```

**응답:**
```json
{
  "jsonrpc": "2.0",
  "result": {
    "audio_base64": "UklGRi...",
    "sample_rate": 24000,
    "format": "wav"
  },
  "id": 1
}
```

---

## 📁 파일 구조

```
c:\git\alienMiniTools\QwenTTS\
├── run.bat                        # 기존 (TTS 서버 실행)
├── run_with_api.bat               # TTS 서버 + API 래퍼 동시 실행
├── Qwen3-TTS-apiwrap/             # 새로 생성 (API 래퍼 + 웹 챗)
│   ├── pyproject.toml             # uv 의존성 관리
│   ├── app.py                     # FastAPI 앱 + MCP 통합
│   ├── tts_client.py              # Gradio 클라이언트 래퍼
│   ├── run.bat                    # 웹 챗 실행 스크립트
│   ├── static/
│   │   ├── style.css              # 스타일
│   │   └── app.js                 # 프론트엔드 로직
│   └── templates/
│       └── index.html             # 채팅 UI
└── Qwen3-TTS/                     # 기존
```

---

## 🔧 기술 스택

| 구성 요소 | 기술 |
|-----------|------|
| TTS 연결 | `gradio_client` |
| 웹 서버 | FastAPI + FastMCP (웹 UI + MCP 통합) |
| 프론트엔드 | Vanilla JS + CSS (Glassmorphism 다크 테마) |
| 프로토콜 | MCP (Streamable HTTP) over n8n |
| 오디오 처리 | scipy (WAV 저장) |

---

## ⚙️ 의존성

`uv`를 사용하여 의존성 관리 (pyproject.toml 기반)

```toml
# pyproject.toml
[project]
name = "qwen3-tts-apiwrap"
version = "0.1.0"
requires-python = ">=3.12"
dependencies = [
    "fastapi>=0.109.0",
    "uvicorn>=0.27.0",
    "gradio_client>=0.10.0",
    "fastmcp",
    "jinja2",
    "aiofiles",
    "scipy",
    "numpy",
]
```

---

## 🚀 실행 방법

1. **TTS 서버 먼저 실행:**
   ```batch
   run.bat
   ```

2. **웹 챗 앱 실행 (uv 자동 의존성 설치):**
   ```batch
   Qwen3-TTS-apiwrap\run.bat
   ```

3. **디버그 모드 실행 (상세 로그 출력):**
   ```batch
   Qwen3-TTS-apiwrap\run.bat --verbose
   ```

4. **접속:**
   - **TTS 서버**: `http://localhost:23015`
   - **MCP 서버** (n8n): `POST http://localhost:23016/mcp` (streamable-http)
   - **웹 챗 UI**: `http://localhost:23007`

---

## 📌 고려 사항

1. **Gradio API 연결**: `gradio_client`가 `http://localhost:23015`에 연결
2. **동시 요청**: FastAPI 앱에서 TTS 요청은 순차 처리 (TTS 모델이 GPU 점유)
3. **오디오 스트리밍**: 생성 완료 후 전체 WAV 반환 (스트리밍 아님)
4. **에러 핸들링**: TTS 서버 다운 시 적절한 에러 메시지
5. **디버그 모드**: `--verbose` 옵션으로 실행 시 상세 로그 출력 (요청/응답 내용, 타이밍, 에러 스택 등)

--
