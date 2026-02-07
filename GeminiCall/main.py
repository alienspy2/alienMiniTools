from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import PlainTextResponse
from contextlib import asynccontextmanager
import uvicorn
import sys
import logging
import os
from datetime import datetime, timezone, timedelta
import asyncio

# Local imports
from config_loader import load_config
from queue_manager import QueueManager
from schema import (
    OllamaChatRequest, OllamaChatResponse, ChatMessage,
    OllamaGenerateRequest, OllamaGenerateResponse,
    OllamaTagsResponse, OllamaModelInfo, OllamaModelDetails, OllamaVersionResponse,
    OllamaShowRequest, OllamaShowResponse,
    OllamaProcessModel, OllamaProcessResponse,
    RPCRequest, RPCResponse,
    McpServerAddRequest, McpServerAddResponse,
    McpServerRemoveRequest, McpServerRemoveResponse,
    McpServerListResponse, McpServerInfo,
    McpRPCRequest,
)
from mcp_service import McpServerRegistry, McpToolService

# Setup Logger
LOG_LEVEL = logging.DEBUG if os.environ.get("GEMINICALL_VERBOSE") else logging.INFO
logging.basicConfig(
    level=LOG_LEVEL,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger("GeminiCall")
logger.setLevel(LOG_LEVEL)

@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    try:
        config = load_config()
        app.state.config = config
        app.state.qm = QueueManager()
        await app.state.qm.start()

        # MCP 초기화
        script_dir = os.path.dirname(os.path.abspath(__file__))
        app.state.mcp_registry = McpServerRegistry(config_dir=script_dir)
        app.state.mcp_tool_service = McpToolService()
        logger.info(f"Loaded {len(app.state.mcp_registry.list_all())} MCP servers from mcp.json")

        logger.info(f"Server started on port {config['http_port']} with RPM {config['rpm']}")
    except Exception as e:
        logger.error(f"Startup failed: {e}")
        sys.exit(1)

    yield

    # Shutdown
    if hasattr(app.state, 'qm'):
        await app.state.qm.stop()
    logger.info("Server shut down.")

app = FastAPI(lifespan=lifespan)

# --- Helper ---
def _model_details(model_name: str) -> OllamaModelDetails:
    family = "gemma" if model_name.lower().startswith("gemma") else "gemini"
    return OllamaModelDetails(family=family, families=[family])

# --- Ollama-compatible Endpoints ---

@app.get("/", response_class=PlainTextResponse)
@app.head("/", response_class=PlainTextResponse)
async def root():
    return "Ollama is running"

@app.get("/api/version", response_model=OllamaVersionResponse)
async def get_version():
    return {"version": "0.1.0"}

@app.get("/api/tags", response_model=OllamaTagsResponse)
async def list_models():
    models = app.state.config.get('models', [])
    model_infos = []
    now_iso = datetime.now(timezone.utc).isoformat()

    for m in models:
        model_infos.append(OllamaModelInfo(
            name=m,
            model=m,
            modified_at=now_iso,
            size=0,
            digest="sha256:0000000000000000000000000000000000000000000000000000000000000000",
            details=_model_details(m)
        ))
    return {"models": model_infos}

@app.post("/api/show", response_model=OllamaShowResponse)
async def show_model(request: OllamaShowRequest):
    model_name = request.model or request.name
    if not model_name:
        raise HTTPException(status_code=400, detail="model is required")

    models = app.state.config.get('models', [])
    if model_name not in models:
        raise HTTPException(status_code=404, detail=f"model '{model_name}' not found")

    now_iso = datetime.now(timezone.utc).isoformat()
    return OllamaShowResponse(
        details=_model_details(model_name),
        model_info={"general.architecture": _model_details(model_name).family},
        modified_at=now_iso
    )

@app.get("/api/ps", response_model=OllamaProcessResponse)
async def list_running_models():
    models = app.state.config.get('models', [])
    running = []
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat()

    for m in models:
        running.append(OllamaProcessModel(
            name=m,
            model=m,
            size=0,
            digest="sha256:0000000000000000000000000000000000000000000000000000000000000000",
            details=_model_details(m),
            expires_at=expires,
            size_vram=0
        ))
    return {"models": running}

@app.get("/health")
async def health_check():
    if not hasattr(app.state, 'qm'):
        return {"status": "starting"}
    status = app.state.qm.get_status()
    return {"status": "ok", **status}

@app.post("/api/chat", response_model=OllamaChatResponse)
async def chat(request: OllamaChatRequest):
    logger.debug(f"Chat request: {request.model}, options={request.options}")
    qm = app.state.qm

    # Extract options
    options = {}
    if request.options:
        options.update(request.options)

    # Convert Pydantic messages to list of dicts
    messages_dict = [m.model_dump() for m in request.messages]

    try:
        if logger.isEnabledFor(logging.DEBUG):
             logger.debug(f"Messages: {messages_dict}")

        future = await qm.submit_request(request.model, messages_dict, options)
        result_text = await future

        logger.debug(f"Chat result (first 50 chars): {result_text[:50]}...")

        now_iso = datetime.now(timezone.utc).isoformat()
        return OllamaChatResponse(
            model=request.model,
            created_at=now_iso,
            message=ChatMessage(role="assistant", content=result_text),
            done=True,
            done_reason="stop"
        )

    except Exception as e:
        logger.error(f"Chat error: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/generate", response_model=OllamaGenerateResponse)
async def generate(request: OllamaGenerateRequest):
    logger.debug(f"Generate request: {request.model}, prompt={request.prompt[:50]}...")
    qm = app.state.qm

    options = {}
    if request.options:
        options.update(request.options)

    messages = []
    if request.system:
        messages.append({"role": "system", "content": request.system})
    messages.append({"role": "user", "content": request.prompt})

    try:
        future = await qm.submit_request(request.model, messages, options)
        result_text = await future

        logger.debug(f"Generate result (first 50 chars): {result_text[:50]}...")

        now_iso = datetime.now(timezone.utc).isoformat()
        return OllamaGenerateResponse(
            model=request.model,
            created_at=now_iso,
            response=result_text,
            done=True,
            done_reason="stop"
        )
    except Exception as e:
        logger.error(f"Generate error: {e}")
        raise HTTPException(status_code=500, detail=str(e))

# --- JSON-RPC 2.0 Endpoint ---

@app.post("/generate", response_model=RPCResponse)
async def json_rpc_handler(request: RPCRequest):
    logger.debug(f"RPC request: method={request.method}, id={request.id}, params={request.params}")

    if request.jsonrpc != "2.0":
         logger.warning(f"Invalid JSON-RPC version: {request.jsonrpc}")
         return RPCResponse(id=request.id, error={"code": -32600, "message": "Invalid Request"})

    if request.method != "generate_content":
        return RPCResponse(id=request.id, error={"code": -32601, "message": "Method not found"})

    qm = app.state.qm
    params = request.params

    # Map RPC params to service format
    try:
        adapted_messages = []
        for msg in params.messages:
            content = ""
            if 'parts' in msg and isinstance(msg['parts'], list):
                content = "".join(str(p) for p in msg['parts'])
            elif 'content' in msg:
                content = msg['content']

            adapted_messages.append({
                "role": msg.get('role', 'user'),
                "content": content
            })

        options = {}
        if params.temperature is not None:
            options['temperature'] = params.temperature
        if params.max_output_tokens is not None:
            options['max_output_tokens'] = params.max_output_tokens

        # Determine model: params.model -> config default
        target_model = params.model
        if not target_model:
            target_model = app.state.config['models'][0]

        if logger.isEnabledFor(logging.DEBUG):
             logger.debug(f"Adapted messages: {adapted_messages}")

        future = await qm.submit_request(target_model, adapted_messages, options)
        result_text = await future

        logger.debug(f"RPC result (first 50 chars): {result_text[:50]}...")

        return RPCResponse(
            id=request.id,
            result={
                "generated_text": result_text,
                "finish_reason": "STOP"
            }
        )

    except Exception as e:
        logger.error(f"RPC Error: {e}")
        return RPCResponse(id=request.id, error={"code": -32603, "message": "Internal error", "data": str(e)})

# --- MCP Server Management Endpoints ---

@app.post("/mcp/add", response_model=McpServerAddResponse)
async def mcp_add_server(request: McpServerAddRequest):
    registry: McpServerRegistry = app.state.mcp_registry
    entry = registry.add(request.name, request.url)
    logger.info(f"MCP server registered: {request.name} -> {request.url}")
    return McpServerAddResponse(name=entry["name"], url=entry["url"])

@app.delete("/mcp/remove", response_model=McpServerRemoveResponse)
async def mcp_remove_server(request: McpServerRemoveRequest):
    registry: McpServerRegistry = app.state.mcp_registry
    if not registry.remove(request.name):
        raise HTTPException(status_code=404, detail=f"MCP server '{request.name}' not found")
    logger.info(f"MCP server removed: {request.name}")
    return McpServerRemoveResponse(name=request.name)

@app.get("/mcp/list", response_model=McpServerListResponse)
async def mcp_list_servers():
    registry: McpServerRegistry = app.state.mcp_registry
    servers = registry.list_all()
    return McpServerListResponse(
        servers=[McpServerInfo(name=s["name"], url=s["url"]) for s in servers]
    )

# --- MCP + Generate Endpoint ---

def _adapt_rpc_messages(messages: list[dict]) -> list[dict]:
    """RPC 메시지를 내부 포맷으로 변환."""
    adapted = []
    for msg in messages:
        content = ""
        if 'parts' in msg and isinstance(msg['parts'], list):
            content = "".join(str(p) for p in msg['parts'])
        elif 'content' in msg:
            content = msg['content']
        adapted.append({
            "role": msg.get('role', 'user'),
            "content": content
        })
    return adapted

@app.post("/generate_with_mcp", response_model=RPCResponse)
async def generate_with_mcp(request: McpRPCRequest):
    logger.debug(f"MCP RPC request: method={request.method}, id={request.id}")
    if logger.isEnabledFor(logging.DEBUG):
        logger.debug(f"  params: model={request.params.model}, "
                     f"mcp_servers={request.params.mcp_servers}, "
                     f"max_iterations={request.params.max_iterations}, "
                     f"temperature={request.params.temperature}, "
                     f"max_output_tokens={request.params.max_output_tokens}")
        logger.debug(f"  messages ({len(request.params.messages)}개):")
        for i, msg in enumerate(request.params.messages):
            role = msg.get('role', '?')
            content = msg.get('content', '')
            preview = content[:100] + '...' if len(str(content)) > 100 else content
            logger.debug(f"    [{i}] {role}: {preview}")

    if request.jsonrpc != "2.0":
        return RPCResponse(id=request.id, error={"code": -32600, "message": "Invalid Request"})

    if request.method != "generate_content":
        return RPCResponse(id=request.id, error={"code": -32601, "message": "Method not found"})

    registry: McpServerRegistry = app.state.mcp_registry
    mcp_svc: McpToolService = app.state.mcp_tool_service
    qm: QueueManager = app.state.qm
    params = request.params

    try:
        # 사용할 MCP 서버 URL 결정
        if params.mcp_servers:
            server_urls = {}
            for name in params.mcp_servers:
                srv = registry.get(name)
                if srv:
                    server_urls[name] = srv["url"]
                else:
                    logger.warning(f"MCP server '{name}' not found, skipping")
        else:
            server_urls = registry.get_all_urls()

        if logger.isEnabledFor(logging.DEBUG):
            logger.debug(f"  resolved server_urls: {server_urls}")

        # MCP 서버 없으면 일반 generate로 폴백
        if not server_urls:
            logger.info("No MCP servers, falling back to normal generation")
            adapted = _adapt_rpc_messages(params.messages)
            target_model = params.model or app.state.config['models'][0]
            options = {}
            if params.temperature is not None:
                options['temperature'] = params.temperature
            if params.max_output_tokens is not None:
                options['max_output_tokens'] = params.max_output_tokens

            logger.debug(f"  fallback generate: model={target_model}, options={options}")
            future = await qm.submit_request(target_model, adapted, options)
            result_text = await future
            logger.debug(f"  fallback result: {result_text[:200]}...")
            return RPCResponse(
                id=request.id,
                result={"generated_text": result_text, "finish_reason": "STOP"}
            )

        # 메시지 변환
        adapted_messages = _adapt_rpc_messages(params.messages)
        target_model = params.model or app.state.config['models'][0]
        options = {}
        if params.temperature is not None:
            options['temperature'] = params.temperature
        if params.max_output_tokens is not None:
            options['max_output_tokens'] = params.max_output_tokens

        max_iter = params.max_iterations or 5
        logger.debug(f"  starting agent loop: model={target_model}, "
                     f"servers={list(server_urls.keys())}, max_iterations={max_iter}, options={options}")

        # ReAct 에이전트 루프 실행
        result_text = await mcp_svc.run_agent_loop(
            qm=qm,
            server_urls=server_urls,
            model=target_model,
            messages=adapted_messages,
            options=options,
            max_iterations=max_iter,
        )

        logger.debug(f"  agent loop finished, result length={len(result_text)}")
        logger.debug(f"  result: {result_text[:300]}")

        return RPCResponse(
            id=request.id,
            result={"generated_text": result_text, "finish_reason": "STOP"}
        )

    except Exception as e:
        logger.error(f"MCP RPC Error: {e}", exc_info=True)
        return RPCResponse(
            id=request.id,
            error={"code": -32603, "message": "Internal error", "data": str(e)}
        )

if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--verbose", "-v", action="store_true", help="Enable verbose logging")
    args = parser.parse_args()

    if args.verbose:
        os.environ["GEMINICALL_VERBOSE"] = "1"
        # Update current logger if already initialized
        logging.getLogger().setLevel(logging.DEBUG)
        logger.setLevel(logging.DEBUG)
        logger.info("Verbose mode enabled")

    # If run directly
    config = load_config()
    uvicorn.run("main:app", host="0.0.0.0", port=config['http_port'], reload=True)
