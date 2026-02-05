# coding: utf-8
"""
Qwen3-TTS API Wrapper - FastAPI + MCP (JSON-RPC) Server

Provides:
- Web chat UI for TTS
- JSON-RPC endpoint for MCP integration
"""

import argparse
import asyncio
import json
import logging
import sys
import time
from pathlib import Path
from typing import Any, Optional

from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from jsonrpcserver import Result, Success, Error, async_dispatch, method
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
    title="Qwen3-TTS API Wrapper",
    description="Web chat UI and MCP JSON-RPC endpoint for Qwen3-TTS",
    version="0.1.0",
)

# Mount static files
app.mount("/static", StaticFiles(directory=STATIC_DIR), name="static")

# Templates
templates = Jinja2Templates(directory=TEMPLATES_DIR)

# TTS Client (initialized on startup)
tts_client: Optional[TTSClient] = None
verbose_mode: bool = False


# =============================================================================
# JSON-RPC Methods (MCP)
# =============================================================================

@method
async def tts_generate(
    text: str,
    language: Optional[str] = None,
    speaker: Optional[str] = None,
    instruct: Optional[str] = None,
) -> Result:
    """
    Generate TTS audio from text.

    Args:
        text: Text to synthesize
        language: Language code (default: Korean)
        speaker: Speaker name (default: Sohee)
        instruct: Optional voice style instruction

    Returns:
        Result with audio_base64, sample_rate, format
    """
    global tts_client, verbose_mode

    if tts_client is None:
        return Error(code=-32000, message="TTS client not initialized")

    if not text or not text.strip():
        return Error(code=-32602, message="Text is required")

    start_time = time.time()

    try:
        if verbose_mode:
            logger.debug(f"RPC tts.generate: text={text[:50]}...")

        # Run TTS in thread pool to avoid blocking
        loop = asyncio.get_event_loop()
        base64_wav, sample_rate = await loop.run_in_executor(
            None,
            tts_client.generate_base64,
            text,
            language,
            speaker,
            instruct,
        )

        elapsed = time.time() - start_time

        if verbose_mode:
            logger.debug(f"TTS completed in {elapsed:.2f}s, audio size: {len(base64_wav)} chars")

        return Success({
            "audio_base64": base64_wav,
            "sample_rate": sample_rate,
            "format": "wav",
        })

    except Exception as e:
        logger.error(f"TTS generation failed: {e}", exc_info=verbose_mode)
        return Error(code=-32000, message=str(e))


# =============================================================================
# HTTP Endpoints
# =============================================================================

@app.get("/", response_class=HTMLResponse)
async def index(request: Request):
    """Serve the web chat UI."""
    return templates.TemplateResponse("index.html", {"request": request})


@app.post("/rpc")
async def rpc_endpoint(request: Request):
    """JSON-RPC endpoint for MCP."""
    body = await request.body()

    if verbose_mode:
        logger.debug(f"RPC request: {body.decode()[:200]}...")

    response = await async_dispatch(body.decode())

    if verbose_mode:
        logger.debug(f"RPC response: {str(response)[:200]}...")

    return JSONResponse(
        content=json.loads(response),
        media_type="application/json",
    )


@app.post("/api/tts")
async def api_tts(request: Request):
    """REST API endpoint for TTS (simpler alternative to JSON-RPC)."""
    global tts_client, verbose_mode

    if tts_client is None:
        return JSONResponse(
            status_code=503,
            content={"error": "TTS client not initialized"},
        )

    try:
        data = await request.json()
        text = data.get("text", "")
        language = data.get("language")
        speaker = data.get("speaker")
        instruct = data.get("instruct")

        if not text.strip():
            return JSONResponse(
                status_code=400,
                content={"error": "Text is required"},
            )

        start_time = time.time()

        # Run TTS in thread pool
        loop = asyncio.get_event_loop()
        base64_wav, sample_rate = await loop.run_in_executor(
            None,
            tts_client.generate_base64,
            text,
            language,
            speaker,
            instruct,
        )

        elapsed = time.time() - start_time

        if verbose_mode:
            logger.info(f"TTS completed: {len(text)} chars -> {len(base64_wav)} base64 chars in {elapsed:.2f}s")

        return JSONResponse(content={
            "audio_base64": base64_wav,
            "sample_rate": sample_rate,
            "format": "wav",
        })

    except Exception as e:
        logger.error(f"API TTS failed: {e}", exc_info=verbose_mode)
        return JSONResponse(
            status_code=500,
            content={"error": str(e)},
        )


@app.get("/api/health")
async def health():
    """Health check endpoint."""
    return {"status": "ok", "tts_connected": tts_client is not None}


# =============================================================================
# Startup / Shutdown
# =============================================================================

@app.on_event("startup")
async def startup():
    """Initialize TTS client on startup."""
    global tts_client, verbose_mode

    # Get settings from environment or use defaults
    tts_server = "http://localhost:23005"

    logger.info(f"Connecting to TTS server: {tts_server}")
    tts_client = TTSClient(tts_server, verbose=verbose_mode)

    logger.info("Qwen3-TTS API Wrapper started")
    logger.info(f"Web UI: http://localhost:23006")
    logger.info(f"MCP endpoint: POST http://localhost:23006/rpc")


@app.on_event("shutdown")
async def shutdown():
    """Cleanup on shutdown."""
    global tts_client
    if tts_client:
        tts_client.close()
    logger.info("Qwen3-TTS API Wrapper stopped")


# =============================================================================
# Main
# =============================================================================

def main():
    """Main entry point."""
    global verbose_mode

    parser = argparse.ArgumentParser(
        description="Qwen3-TTS API Wrapper - Web chat UI and MCP server"
    )
    parser.add_argument(
        "--host",
        default="0.0.0.0",
        help="Host to bind (default: 0.0.0.0)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=23006,
        help="Port to bind (default: 23006)",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Enable verbose debug logging",
    )

    args = parser.parse_args()

    verbose_mode = args.verbose

    if verbose_mode:
        logging.getLogger().setLevel(logging.DEBUG)
        logger.info("Verbose mode enabled")

    uvicorn.run(
        app,
        host=args.host,
        port=args.port,
        log_level="debug" if verbose_mode else "info",
    )


if __name__ == "__main__":
    main()
