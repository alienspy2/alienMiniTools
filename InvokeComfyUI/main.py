import json
import os
import time
import uuid
import logging

import requests
from fastapi import FastAPI
from pydantic import BaseModel

COMFY_URL = os.environ.get("COMFY_URL", "http://127.0.0.1:8000")
WORKFLOW_PATH = os.environ.get("WORKFLOW_PATH", "c.json")
OUTPUT_DIR = os.environ.get("OUTPUT_DIR", "output")
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "gemma3:4b")
POLL_INTERVAL_SECONDS = float(os.environ.get("POLL_INTERVAL_SECONDS", "2"))
TIMEOUT_SECONDS = int(os.environ.get("TIMEOUT_SECONDS", "600"))
REQUEST_TIMEOUT_SECONDS = int(os.environ.get("REQUEST_TIMEOUT_SECONDS", "600"))

app = FastAPI()
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("comfy-api")


class JsonRpcRequest(BaseModel):
    jsonrpc: str
    method: str
    params: dict | None = None
    id: int | str | None = None


def load_workflow() -> dict:
    logger.info("load_workflow path=%s", WORKFLOW_PATH)
    with open(WORKFLOW_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def set_positive_prompt(workflow: dict, prompt: str) -> bool:
    logger.info("set_positive_prompt prompt_len=%d", len(prompt))
    for node in workflow.values():
        if node.get("class_type") != "CLIPTextEncode":
            continue
        meta = node.get("_meta") or {}
        title = str(meta.get("title", ""))
        if "Positive Prompt" in title:
            node.setdefault("inputs", {})["text"] = prompt
            return True
    return False


def queue_prompt(prompt_workflow: dict) -> dict:
    logger.info("queue_prompt url=%s", f"{COMFY_URL}/prompt")
    payload = {"prompt": prompt_workflow}
    response = requests.post(
        f"{COMFY_URL}/prompt",
        json=payload,
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    logger.info("queue_prompt status=%s", response.status_code)
    response.raise_for_status()
    return response.json()


def save_debug_workflow(workflow: dict) -> None:
    workflow_name = os.path.basename(WORKFLOW_PATH)
    debug_name = f"debug_modified_{workflow_name}"
    logger.info("save_debug_workflow path=%s", debug_name)
    with open(debug_name, "w", encoding="utf-8") as f:
        json.dump(workflow, f, ensure_ascii=False, indent=2)


def refine_prompt(prompt: str) -> str:
    instructions = (
        "Translate the user prompt to English and rewrite it as a concise, vivid "
        "image-generation prompt. Keep it to one line, no quotes, no markdown, "
        "no extra commentary."
    )
    logger.info("refine_prompt model=%s url=%s", OLLAMA_MODEL, OLLAMA_URL)
    payload = {
        "model": OLLAMA_MODEL,
        "prompt": f"{instructions}\n\nUser prompt: {prompt}",
        "stream": False,
    }
    response = requests.post(
        f"{OLLAMA_URL}/api/generate",
        json=payload,
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    logger.info("refine_prompt status=%s", response.status_code)
    response.raise_for_status()
    data = response.json()
    refined = str(data.get("response", "")).strip()
    if not refined:
        raise ValueError("Ollama returned an empty prompt.")
    logger.info("refine_prompt result_len=%d", len(refined))
    return refined


def fetch_history(prompt_id: str) -> dict:
    logger.info("fetch_history prompt_id=%s", prompt_id)
    response = requests.get(
        f"{COMFY_URL}/history/{prompt_id}",
        timeout=REQUEST_TIMEOUT_SECONDS,
    )
    logger.info("fetch_history status=%s", response.status_code)
    response.raise_for_status()
    return response.json()


def extract_images(history_entry: dict) -> list[dict]:
    images = []
    outputs = history_entry.get("outputs", {})
    for node_data in outputs.values():
        for image in node_data.get("images", []):
            filename = image.get("filename")
            if not filename:
                continue
            subfolder = image.get("subfolder", "")
            image_type = image.get("type", "output")
            base_dir = "output" if image_type == "output" else image_type
            if subfolder:
                path = os.path.join(base_dir, subfolder, filename)
            else:
                path = os.path.join(base_dir, filename)
            images.append(
                {
                    "filename": filename,
                    "subfolder": subfolder,
                    "type": image_type,
                    "path": path,
                }
            )
    return images


def download_images(images: list[dict], prompt_id: str) -> list[dict]:
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    saved = []
    for index, image in enumerate(images, start=1):
        filename = image.get("filename")
        if not filename:
            continue
        subfolder = image.get("subfolder", "")
        image_type = image.get("type", "output")
        url = (
            f"{COMFY_URL}/view?filename={filename}"
            f"&subfolder={subfolder}&type={image_type}"
        )
        response = requests.get(url, timeout=REQUEST_TIMEOUT_SECONDS)
        response.raise_for_status()
        local_name = f"{prompt_id}_{index}_{filename}"
        local_path = os.path.join(OUTPUT_DIR, local_name)
        with open(local_path, "wb") as f:
            f.write(response.content)
        saved.append(
            {
                "filename": local_name,
                "subfolder": "",
                "type": "local",
                "path": local_path,
            }
        )
    return saved


def wait_for_outputs(prompt_id: str, timeout_seconds: int) -> dict:
    logger.info("wait_for_outputs prompt_id=%s timeout=%s", prompt_id, timeout_seconds)
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        history = fetch_history(prompt_id)
        entry = history.get(prompt_id)
        if entry and entry.get("outputs"):
            return entry
        time.sleep(POLL_INTERVAL_SECONDS)
    raise TimeoutError("Timed out waiting for ComfyUI outputs.")


def make_error(code: int, message: str, req_id) -> dict:
    logger.error("jsonrpc_error code=%s message=%s req_id=%s", code, message, req_id)
    return {
        "jsonrpc": "2.0",
        "error": {"code": code, "message": message},
        "id": req_id,
    }


@app.post("/")
def json_rpc(request: JsonRpcRequest) -> dict:
    request_id = uuid.uuid4().hex[:8]
    logger.info("[%s] request method=%s", request_id, request.method)
    if request.jsonrpc != "2.0":
        return make_error(-32600, "Invalid JSON-RPC version.", request.id)
    if request.method != "generate":
        return make_error(-32601, "Method not found.", request.id)

    params = request.params or {}
    prompt = params.get("prompt")
    if not isinstance(prompt, str) or not prompt.strip():
        return make_error(-32602, "Invalid params: 'prompt' is required.", request.id)

    try:
        logger.info("[%s] prompt_len=%d", request_id, len(prompt))
        refined_prompt = refine_prompt(prompt)
        logger.info("[%s] refined_prompt=%s", request_id, refined_prompt)
        workflow = load_workflow()
        if not set_positive_prompt(workflow, refined_prompt):
            return make_error(-32000, "Positive prompt node not found.", request.id)

        save_debug_workflow(workflow)
        queued = queue_prompt(workflow)
        prompt_id = queued.get("prompt_id")
        if not prompt_id:
            return make_error(-32001, "ComfyUI did not return prompt_id.", request.id)
        logger.info("[%s] prompt_id=%s waiting_for_outputs", request_id, prompt_id)

        history_entry = wait_for_outputs(prompt_id, TIMEOUT_SECONDS)
        images = extract_images(history_entry)
        images = download_images(images, prompt_id)
        logger.info("[%s] outputs_ready images=%d", request_id, len(images))

        return {
            "jsonrpc": "2.0",
            "result": {"prompt_id": prompt_id, "images": images},
            "id": request.id,
        }
    except TimeoutError as exc:
        return make_error(-32002, str(exc), request.id)
    except requests.RequestException as exc:
        logger.exception("[%s] ComfyUI error", request_id)
        return make_error(-32003, f"ComfyUI request failed: {exc}", request.id)
    except Exception as exc:
        logger.exception("[%s] unexpected error", request_id)
        return make_error(-32099, f"Unexpected error: {exc}", request.id)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8001, log_level="info")
