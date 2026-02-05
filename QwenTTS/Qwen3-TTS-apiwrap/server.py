# coding: utf-8
"""
Qwen3-TTS MCP Server (FastMCP)

Provides:
- MCP Server via fastmcp (supporting streamable-http)
- Port: 23006
"""

import argparse
import asyncio
import json
import logging
import time
from typing import Optional, Literal
from pathlib import Path

from fastmcp import FastMCP, Context

from tts_client import TTSClient

# App paths
BASE_DIR = Path(__file__).parent


# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logging.getLogger("fakeredis").setLevel(logging.WARNING)
logging.getLogger("docket").setLevel(logging.WARNING)
logging.getLogger("httpx").setLevel(logging.WARNING)  # Gradio client HTTP noise
logger = logging.getLogger(__name__)

# TTS Client Global
tts_client: Optional[TTSClient] = None
verbose_mode: bool = False
PUBLIC_HOST: Optional[str] = None
WEBHOOK_URL: Optional[str] = None

# Initialize FastMCP
mcp = FastMCP("Qwen3-TTS")

# =============================================================================
# MCP Tools
# =============================================================================

import os
import uuid
import socket
import httpx

# ... (기존 imports 유지)

# Ensure static/generated directory exists
GENERATED_DIR = BASE_DIR / "static" / "generated"
GENERATED_DIR.mkdir(parents=True, exist_ok=True)

def get_local_ip():
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
        s.close()
        return ip
    except Exception:
        return "127.0.0.1"

def send_webhook(url: str, file_path: Path, metadata: dict):
    """Send the generated audio file to a webhook."""
    try:
        with open(file_path, "rb") as f:
            files = {"file": (file_path.name, f, "audio/wav")}
            # Send metadata as form fields
            data = {k: str(v) for k, v in metadata.items()}
            logger.info(f"Sending webhook to {url}...")
            response = httpx.post(url, data=data, files=files, timeout=30.0)
            response.raise_for_status()
            logger.info(f"Webhook sent successfully: {response.status_code}")
    except Exception as e:
        logger.error(f"Failed to send webhook: {e}")

import threading

def generate_speech_task(text, language, speaker, instruct, webhook_url, public_host):
    """Background task for TTS generation and webhook delivery."""
    global tts_client, verbose_mode
    
    # Re-initialize tts_client here if needed or ensure thread safety
    # TTS generation might block, so running in thread is good.
    try:
        # Generate
        start_time = time.time()
        wav_bytes, sample_rate = tts_client.generate(
            text, language, speaker, instruct or ""
        )
        
        # Save
        filename = f"tts{uuid.uuid4().hex[:8]}.wav"
        file_path = GENERATED_DIR / filename
        with open(file_path, "wb") as f:
            f.write(wav_bytes)
            
        elapsed = time.time() - start_time
        logger.info(f"Background TTS finished in {elapsed:.2f}s: {filename}")
        
        # URL
        host_ip = public_host if public_host else get_local_ip()
        audio_url = f"http://{host_ip}:23007/static/generated/{filename}"
        
        result = {
            "status": "success",
            "message": "Audio generated successfully.",
            "audio_url": audio_url,
            "duration_sec": len(wav_bytes) / (sample_rate * 2),
            "filename": filename
        }
        
        # Webhook
        if webhook_url:
            send_webhook(webhook_url, file_path, result)
            
    except Exception as e:
        logger.error(f"Background TTS failed: {e}", exc_info=True)
        # Optionally send failure webhook?

@mcp.tool()
def generate_speech(
    text: str,
    language: Literal["Korean", "English", "Chinese", "Japanese", "Auto"] = "Korean",
    speaker: str = "Sohee", 
    instruct: str = "",
    toolCallId: str = "",
    id: str = "",
    ctx: Context = None,
) -> str:
    """
    Initiate TTS generation in background.
    Returns immediately. The result will be sent via Webhook.
    """
    global tts_client, verbose_mode, PUBLIC_HOST, WEBHOOK_URL

    if tts_client is None:
        tts_client = TTSClient("http://localhost:23005", verbose=verbose_mode)
    
    # Start background thread
    t = threading.Thread(
        target=generate_speech_task,
        args=(text, language, speaker, instruct, WEBHOOK_URL, PUBLIC_HOST)
    )
    t.start()
    
    return json.dumps({
        "status": "queued", 
        "message": "TTS generation started in background. Result will be sent via webhook."
    })

# =============================================================================
# Main Execution
# =============================================================================

def main():
    global verbose_mode, tts_client
    
    # ... main 함수 내부 ...
    parser = argparse.ArgumentParser(description="Qwen3-TTS MCP Server")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=23006)
    parser.add_argument("--verbose", action="store_true")
    # Add public host argument
    parser.add_argument("--public-host", default=None, help="Public IP/Hostname for Web UI links")
    parser.add_argument("--webhook-url", default=None, help="Webhook URL to send generated audio")
    args = parser.parse_args()
    
    # Set global variable for tool to use
    global PUBLIC_HOST, WEBHOOK_URL
    PUBLIC_HOST = args.public_host
    WEBHOOK_URL = args.webhook_url


    verbose_mode = args.verbose
    if verbose_mode:
        logging.getLogger().setLevel(logging.DEBUG)
        # Suppress noise
        logging.getLogger("fakeredis").setLevel(logging.WARNING)
        logging.getLogger("docket").setLevel(logging.WARNING)
        logging.getLogger("httpx").setLevel(logging.WARNING)
        logging.getLogger("fastmcp").setLevel(logging.INFO)

    logger.info(f"Starting Qwen3-TTS MCP Server on {args.host}:{args.port}")
    if PUBLIC_HOST:
         logger.info(f"Public Host override: {PUBLIC_HOST}")

    # ... (나머지 동일)
    logger.info(f"MCP Endpoint: http://{args.host}:{args.port}/mcp")

    # Pre-initialize TTS Client
    try:
        tts_client = TTSClient("http://localhost:23005", verbose=verbose_mode)
        logger.info("TTS Client connected successfully")
    except Exception as e:
        logger.warning(f"Could not connect to TTS server at startup: {e}")
        logger.warning("Will retry on first request.")

    mcp.run(
        transport="streamable-http",
        host=args.host,
        port=args.port,
        path="/mcp",
    )

if __name__ == "__main__":
    main()
