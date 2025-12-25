import json
import os
import signal
import logging
from pathlib import Path
from urllib.parse import quote

import requests
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse

COMFY_RPC_URL = "http://127.0.0.1:8001/"
REQUEST_TIMEOUT_SECONDS = 600
OUTPUT_ROOT = Path(os.environ.get("COMFY_OUTPUT_ROOT", ".")).resolve()

app = FastAPI()
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("comfy-client")


HTML_PAGE = """
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>ComfyUI Prompt Chat</title>
    <style>
      :root {
        color-scheme: light;
        --bg: #f6f2ea;
        --panel: #fff8f0;
        --ink: #2b241e;
        --accent: #e05d2f;
        --muted: #6a5d52;
      }
      body {
        margin: 0;
        background: linear-gradient(160deg, #fff1e0, #f8e4d3 45%, #f2efe9 100%);
        font-family: "Space Grotesk", "Segoe UI", sans-serif;
        color: var(--ink);
        min-height: 100vh;
        display: flex;
        justify-content: center;
      }
      .wrap {
        width: min(900px, 100%);
        padding: 32px 20px 40px;
      }
      header {
        display: flex;
        align-items: baseline;
        gap: 16px;
        margin-bottom: 18px;
      }
      header h1 {
        font-size: 28px;
        margin: 0;
        letter-spacing: 0.02em;
      }
      header span {
        font-size: 14px;
        color: var(--muted);
      }
      .chat {
        background: var(--panel);
        border-radius: 20px;
        padding: 20px;
        box-shadow: 0 18px 40px rgba(83, 52, 28, 0.15);
        display: flex;
        flex-direction: column;
        gap: 16px;
        min-height: 380px;
      }
      .message {
        padding: 12px 16px;
        border-radius: 14px;
        max-width: 78%;
        line-height: 1.4;
        word-break: break-word;
      }
      .user {
        align-self: flex-end;
        background: #ffe2c6;
        border: 1px solid #f4c49c;
      }
      .bot {
        align-self: flex-start;
        background: #ffffff;
        border: 1px solid #ead9c6;
      }
      .bot img {
        width: 100%;
        border-radius: 12px;
        margin-top: 10px;
        border: 1px solid #f1e1d3;
      }
      .meta {
        font-size: 12px;
        color: var(--muted);
        margin-top: 6px;
      }
      .prompt-form {
        display: flex;
        gap: 12px;
        margin-top: 18px;
      }
      .prompt-form input {
        flex: 1;
        padding: 12px 14px;
        border-radius: 12px;
        border: 1px solid #d9c8b7;
        font-size: 15px;
        background: #fffdf9;
      }
      .prompt-form button {
        padding: 12px 18px;
        border-radius: 12px;
        border: none;
        background: var(--accent);
        color: white;
        font-weight: 600;
        cursor: pointer;
      }
      .prompt-form button:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
      @media (max-width: 640px) {
        header {
          flex-direction: column;
          align-items: flex-start;
        }
        .message {
          max-width: 100%;
        }
      }
    </style>
  </head>
  <body>
    <div class="wrap">
      <header>
        <h1>ComfyUI Prompt Chat</h1>
        <span>Local client → 8001 JSON-RPC</span>
      </header>
      <div id="chat" class="chat"></div>
      <form id="promptForm" class="prompt-form">
        <input id="promptInput" type="text" placeholder="Describe the image you want..." autocomplete="off" />
        <button id="sendButton" type="submit">Send</button>
      </form>
    </div>
    <script>
      const chat = document.getElementById("chat");
      const form = document.getElementById("promptForm");
      const input = document.getElementById("promptInput");
      const button = document.getElementById("sendButton");

      function addMessage(prompt, role) {
        const div = document.createElement("div");
        div.className = `message ${role}`;
        div.textContent = prompt;
        chat.appendChild(div);
        chat.scrollTop = chat.scrollHeight;
        return div;
      }

      function addBotMessageWithImage(prompt, imageUrl, durationMs) {
        const div = document.createElement("div");
        div.className = "message bot";
        const p = document.createElement("div");
        p.textContent = prompt;
        div.appendChild(p);
        if (imageUrl) {
          const img = document.createElement("img");
          img.src = imageUrl;
          img.alt = "generated result";
          div.appendChild(img);
        }
        if (typeof durationMs === "number") {
          const meta = document.createElement("div");
          meta.className = "meta";
          meta.textContent = `Time: ${(durationMs / 1000).toFixed(2)}s`;
          div.appendChild(meta);
        }
        chat.appendChild(div);
        chat.scrollTop = chat.scrollHeight;
      }

      form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const prompt = input.value.trim();
        if (!prompt) return;
        addMessage(prompt, "user");
        input.value = "";
        button.disabled = true;
        const placeholder = addMessage("Generating...", "bot");

        const startedAt = performance.now();
        try {
          const response = await fetch("/send", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ prompt }),
          });
          const payload = await response.json();
          placeholder.remove();
          if (!response.ok || payload.error) {
            const message = payload.error?.message || "Request failed";
            addMessage(message, "bot");
            return;
          }
          const result = payload.result;
          const firstImage = result.images?.[0];
          const durationMs = performance.now() - startedAt;
          addBotMessageWithImage(
            `Done. prompt_id: ${result.prompt_id}`,
            firstImage?.url || null,
            durationMs
          );
        } catch (err) {
          placeholder.remove();
          addMessage(`Error: ${err}`, "bot");
        } finally {
          button.disabled = false;
        }
      });
    </script>
  </body>
</html>
"""


def call_comfy(prompt: str) -> dict:
    logger.info("call_comfy url=%s prompt_len=%d", COMFY_RPC_URL, len(prompt))
    payload = {
        "jsonrpc": "2.0",
        "method": "generate",
        "params": {"prompt": prompt},
        "id": 1,
    }
    response = requests.post(
        COMFY_RPC_URL,
        json=payload,
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    logger.info("call_comfy status=%s", response.status_code)
    response.raise_for_status()
    return response.json()


def resolve_image_path(raw_path: str) -> Path:
    logger.info("resolve_image_path raw_path=%s", raw_path)
    candidate = Path(raw_path)
    if not candidate.is_absolute():
        candidate = OUTPUT_ROOT / candidate
    return candidate.resolve()


@app.get("/", response_class=HTMLResponse)
def index() -> str:
    return HTML_PAGE


@app.post("/send")
async def send(request: Request) -> JSONResponse:
    logger.info("send request received")
    body = await request.json()
    prompt = (body.get("prompt") or "").strip()
    if not prompt:
        logger.warning("send missing prompt")
        return JSONResponse({"error": {"message": "Prompt is required."}}, status_code=400)

    try:
        rpc_response = call_comfy(prompt)
    except requests.RequestException as exc:
        logger.exception("send rpc failed")
        return JSONResponse(
            {"error": {"message": f"RPC failed: {exc}"}},
            status_code=502,
        )

    if "error" in rpc_response:
        logger.error("send rpc error=%s", rpc_response.get("error"))
        return JSONResponse(rpc_response, status_code=502)

    result = rpc_response.get("result", {})
    images = []
    for image in result.get("images", []):
        raw_path = image.get("path")
        if not raw_path:
            continue
        logger.info("send image path=%s", raw_path)
        url = f"/image?path={quote(raw_path)}"
        images.append({**image, "url": url})

    logger.info("send response images=%d", len(images))
    return JSONResponse({"result": {"prompt_id": result.get("prompt_id"), "images": images}})


@app.get("/image")
def image(path: str):
    logger.info("image request path=%s", path)
    resolved = resolve_image_path(path)
    if not str(resolved).startswith(str(OUTPUT_ROOT)):
        logger.warning("image invalid path resolved=%s", resolved)
        raise HTTPException(status_code=400, detail="Invalid path.")
    if not resolved.exists():
        logger.warning("image not found resolved=%s", resolved)
        raise HTTPException(status_code=404, detail="Image not found.")
    logger.info("image serve resolved=%s", resolved)
    return FileResponse(resolved)


if __name__ == "__main__":
    import uvicorn

    config = uvicorn.Config(app, host="0.0.0.0", port=8002, log_level="info")
    server = uvicorn.Server(config)

    def handle_exit(signum, frame):
        server.should_exit = True

    signal.signal(signal.SIGINT, handle_exit)
    signal.signal(signal.SIGTERM, handle_exit)

    server.run()
