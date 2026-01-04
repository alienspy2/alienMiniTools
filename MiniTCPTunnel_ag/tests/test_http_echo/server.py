from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse, HTMLResponse
import uvicorn
import os
import logging
import sys

# Windows 기본 인코딩(cp949)로 출력되면 로그가 깨질 수 있으므로,
# stdout/stderr를 UTF-8로 재설정해 리다이렉션 파일도 UTF-8로 저장되게 한다.
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    # 일부 환경에서는 reconfigure가 없을 수 있으니 안전하게 무시한다.
    pass

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    stream=sys.stdout
)
logger = logging.getLogger("echo_server")

app = FastAPI()

# Simple In-Memory JSON-RPC Handler
@app.post("/rpc")
async def rpc_handler(request: Request):
    try:
        data = await request.json()
        logger.info("RPC 요청 수신: %s", data)
        
        # Validate JSON-RPC 2.0
        if data.get("jsonrpc") != "2.0":
            logger.warning("Invalid JSON-RPC version")
            return JSONResponse({"jsonrpc": "2.0", "error": {"code": -32600, "message": "Invalid Request"}, "id": None})
        
        method = data.get("method")
        params = data.get("params")
        req_id = data.get("id")
        
        # Implementation
        if method == "echo":
            # Echo logic
            text = params[0] if isinstance(params, list) and params else params.get("text")
            result = f"Echo: {text}"
            logger.info("RPC echo 처리: %s", text)
            return {
                "jsonrpc": "2.0",
                "result": result,
                "id": req_id
            }
        else:
            logger.warning("RPC method not found: %s", method)
            return {
                "jsonrpc": "2.0",
                "error": {"code": -32601, "message": "Method not found"},
                "id": req_id
            }
            
    except Exception as e:
        logger.exception("RPC 처리 중 예외 발생: %s", e)
        return {
            "jsonrpc": "2.0",
            "error": {"code": -32603, "message": f"Internal error: {str(e)}"},
            "id": None
        }

@app.get("/", response_class=HTMLResponse)
async def serve_client():
    # Serve the index.html from the same folder
    try:
        with open("index.html", "r", encoding='utf-8') as f:
            logger.info("index.html 제공")
            return f.read()
    except FileNotFoundError:
        logger.warning("index.html 없음")
        return "<h1>Client not found (index.html is missing)</h1>"

if __name__ == "__main__":
    # Run on port 8000
    logger.info("Echo 서버 시작: 0.0.0.0:8000")
    uvicorn.run(app, host="0.0.0.0", port=8000, log_level="info")
