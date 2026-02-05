# coding: utf-8
"""
Qwen3-TTS Web UI Server

Provides:
- Web chat UI for TTS
- REST API for frontend
- Port: 23007
"""

import argparse
import asyncio
import logging
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
import uvicorn

from tts_client import TTSClient

# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger(__name__)

# App paths
BASE_DIR = Path(__file__).parent
STATIC_DIR = BASE_DIR / "static"
TEMPLATES_DIR = BASE_DIR / "templates"

# FastAPI app
app = FastAPI(
    title="Qwen3-TTS Web UI",
    description="Web Chat Interface for Qwen3-TTS",
    version="1.0.0",
)

# Mount static files
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")

# Templates
templates = Jinja2Templates(directory=TEMPLATES_DIR)

# TTS Client Global
tts_client: Optional[TTSClient] = None
verbose_mode: bool = False

# =============================================================================
# Routes
# =============================================================================

@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    """Serve the web chat UI."""
    return templates.TemplateResponse("index.html", {"request": request})

@app.post("/api/tts")
async def api_tts(request: Request):
    """REST API endpoint for Web UI."""
    global tts_client, verbose_mode

    if tts_client is None:
        return JSONResponse(status_code=503, content={"error": "TTS client not initialized"})

    try:
        data = await request.json()
        
        # Run in thread pool to avoid blocking
        loop = asyncio.get_event_loop()
        base64_wav, sample_rate = await loop.run_in_executor(
            None,
            tts_client.generate_base64,
            data.get("text", ""),
            data.get("language"),
            data.get("speaker"),
            data.get("instruct"),
        )
        
        return JSONResponse(content={
            "audio_base64": base64_wav,
            "sample_rate": sample_rate,
            "format": "wav",
        })
    except Exception as e:
        logger.error(f"API TTS failed: {e}", exc_info=verbose_mode)
        return JSONResponse(status_code=500, content={"error": str(e)})

@app.get("/api/health")
async def health():
    return {"status": "ok", "tts_connected": tts_client is not None}

# =============================================================================
# Lifecycle
# =============================================================================

@app.on_event("startup")
async def startup():
    """Initialize TTS client on startup."""
    global tts_client, verbose_mode

    tts_server = "http://localhost:23005"
    logger.info(f"Connecting to TTS server: {tts_server}")
    tts_client = TTSClient(tts_server, verbose=verbose_mode)
    logger.info("Web UI Server Ready")

@app.on_event("shutdown")
async def shutdown():
    global tts_client
    if tts_client:
        tts_client.close()

# =============================================================================
# Main
# =============================================================================

def main():
    global verbose_mode

    parser = argparse.ArgumentParser(description="Qwen3-TTS Web UI Server")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=23007)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args()

    verbose_mode = args.verbose
    if verbose_mode:
        logging.getLogger().setLevel(logging.DEBUG)
            
    # Run Uvicorn
    uvicorn.run(
        app,
        host=args.host,
        port=args.port,
        log_level="debug" if verbose_mode else "info",
    )

if __name__ == "__main__":
    main()
