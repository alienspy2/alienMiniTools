# GeminiCall

GeminiCall은 Google GenAI (Gemini) API를 래핑하여 **Ollama 호환 API**와 **JSON-RPC 2.0 API**를 제공하는 경량 서버입니다.
기존 Ollama 지원 툴과 연동하거나, 커스텀 클라이언트에서 Gemini 모델을 쉽게 사용할 수 있도록 돕습니다.

주요 특징:
- **Ollama 호환성**: `/api/chat`, `/api/tags` 등의 엔드포인트를 지원하여 Ollama처럼 동작합니다.
- **JSON-RPC 2.0**: `/generate` 엔드포인트를 통해 구조화된 요청을 처리합니다.
- **안정성**: 요청 큐(Queue)와 속도 제한(RPM) 관리가 내장되어 있습니다.

## 사용법 (Usage)

### 1. 설정 (Configuration)
프로젝트 루트의 `config.json` 파일을 수정하여 API 키와 모델을 설정합니다.

```json
{
    "api_key": "YOUR_GOOGLE_AI_API_KEY",
    "models": [
        "gemini-3-flash-preview",
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemma-3-27b-it",
        "gemma-3-4b-it"
    ],
    "rpm": 15,
    "http_port": 20006
}
```

### 2. 서버 실행 (Run Server)
`uv`를 사용하여 서버를 실행합니다. 아래 배치 파일을 실행하세요.

```cmd
run_server.bat
```
서버는 기본적으로 `http://localhost:20006`에서 실행됩니다.

### 3. 테스트 및 클라이언트 실행

**Ollama 호환 API 테스트:**
Ollama 클라이언트처럼 동작하는 테스트 스크립트입니다.
```cmd
run_test_fake_ollama.bat
```

**JSON-RPC 채팅 클라이언트:**
터미널에서 챗봇과 대화할 수 있는 CLI 클라이언트입니다.
```cmd
run_test_jsonrpc.bat
```

**Gemini TTS 테스트:**
텍스트를 음성으로 변환(TTS)하여 오디오 파일로 저장합니다.
```cmd
run_test_gemini_tts.bat
```

### 4. 필수 요구사항
- Python 3.10+
- `uv` 패키지 매니저 (권장)
