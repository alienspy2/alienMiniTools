from fastapi import FastAPI, HTTPException, Request
from contextlib import asynccontextmanager
import uvicorn
import sys
import logging
import os
from datetime import datetime, timezone
import asyncio

# Local imports
from config_loader import load_config
from queue_manager import QueueManager
from schema import (
    OllamaChatRequest, OllamaChatResponse, ChatMessage,
    OllamaGenerateRequest, OllamaGenerateResponse,
    OllamaTagsResponse, OllamaModelInfo, OllamaVersionResponse,
    RPCRequest, RPCResponse
)

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

# --- Endpoints ---

@app.get("/health")
async def health_check():
    if not hasattr(app.state, 'qm'):
        return {"status": "starting"}
    status = app.state.qm.get_status()
    return {"status": "ok", **status}

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
            size=0, # Unknown
            digest="sha256:0000000000000000000000000000000000000000000000000000000000000000"
        ))
    return {"models": model_infos}

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
        
        # Construct Ollama success response
        now_iso = datetime.now(timezone.utc).isoformat()
        return OllamaChatResponse(
            model=request.model,
            created_at=now_iso,
            message=ChatMessage(role="assistant", content=result_text),
            done=True
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
            done=True
        )
    except Exception as e:
        logger.error(f"Generate error: {e}")
        raise HTTPException(status_code=500, detail=str(e))

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
