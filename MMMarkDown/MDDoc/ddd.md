import argparse
import hashlib
import json
import mimetypes
import os
import shutil
import subprocess
import sys
import threading
import time
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_STATE_FILE = os.environ.get("MMM_FILE", os.path.join(BASE_DIR, "mindmap.mmm"))
DOC_DIR = os.path.join(BASE_DIR, "MDDoc")
STATIC_DIR = os.environ.get("MMM_STATIC_DIR", os.path.join(BASE_DIR, "static"))

OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "gemma3:4b")
SUMMARY_PROMPT = os.environ.get(
    "SUMMARY_PROMPT",
    "아래 마크다운을 한국어로 간결하게 요약해줘. 3~5문장으로 작성하고, '요약:'이나 \"Here's a 3-sentence summary...\" 같은 머리말은 붙이지 말고 내용만 출력해.\n\n",
)
SUMMARY_POLL_SECONDS = float(os.environ.get("SUMMARY_POLL_SECONDS", "3"))
SUMMARY_TIMEOUT_SECONDS = int(os.environ.get("SUMMARY_TIMEOUT_SECONDS", "90"))
OLLAMA_ENABLED = os.environ.get("OLLAMA_ENABLED", "1") != "0"
RELOAD_ENABLED = os.environ.get("MMM_RELOAD", "1") != "0"

INVALID_NAME_CHARS = set('<>:"/\\|?*')
RESERVED_NAMES = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    "COM1",
    "COM2",
    "COM3",
    "COM4",
    "COM5",
    "COM6",
    "COM7",
    "COM8",
    "COM9",
    "LPT1",
    "LPT2",
    "LPT3",
    "LPT4",
    "LPT5",
    "LPT6",
    "LPT7",
    "LPT8",
    "LPT9",
}

def log(message):
    print(f"[MMM] {message}")

def rel_to_abs(rel_path):
    parts = rel_path.replace("\\", "/").split("/")
    return os.path.abspath(os.path.join(BASE_DIR, *parts))

def read_text_file(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            return handle.read()
    except FileNotFoundError:
        return ""
    except UnicodeDecodeError:
        with open(path, "r", encoding="cp949", errors="replace") as handle:
            return handle.read()

def hash_text(text):
    return hashlib.sha256(text.encode("utf-8", errors="replace")).hexdigest()

def safe_write_json(path, payload):
    dir_name = os.path.dirname(path)
    if dir_name:
        os.makedirs(dir_name, exist_ok=True)
    tmp_path = f"{path}.tmp"
    with open(tmp_path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
    os.replace(tmp_path, path)

def normalize_name(raw_name):
    if not isinstance(raw_name, str):
        raise ValueError("Name must be a string.")
    name = raw_name.strip()
    if not name:
        raise ValueError("Name is required.")
    if any(char in INVALID_NAME_CHARS for char in name):
        raise ValueError("Name contains invalid characters.")
    if "/" in name or "\\" in name:
        raise ValueError("Name must not include path separators.")
    if name.endswith("."):
        raise ValueError("Name cannot end with '.'.")
    if len(name) > 120:
        raise ValueError("Name is too long.")
    if not name.lower().endswith(".md"):
        name = f"{name}.md"
    base_name = os.path.splitext(name)[0].upper()
    if base_name in RESERVED_NAMES:
        raise ValueError("Name is reserved on Windows.")
    return name

class MapStore:
    def __init__(self, state_path, doc_dir):
        self.state_path = state_path
        self.doc_dir = doc_dir
        self.lock = threading.Lock()
        self.state = self._load_or_init()

    def _default_state(self):
        root_id = str(uuid.uuid4())
        root_name = "Root.md"
        state = {
            "version": 1,
            "root": root_id,
            "nodes": {
                root_id: {
                    "id": root_id,
                    "name": root_name,
                    "file": f"MDDoc/{root_name}",
                    "summary": "",
                    "content_hash": "",
                    "summary_updated": None,
                    "summary_error": "",
                }
            },
            "edges": [],
        }
        return state
    
    def _normalize_state(self, data):
        if not isinstance(data, dict):
            return self._default_state()
        data.setdefault("version", 1)
        data.setdefault("nodes", {})
        data.setdefault("edges", [])
        if not data.get("nodes"):
            data = self._default_state()
        if "root" not in data or data["root"] not in data["nodes"]:
            data["root"] = next(iter(data["nodes"].keys()))
        for node_id, node in list(data["nodes"].items()):
            node.setdefault("id", node_id)
            node.setdefault("name", f"{node_id}.md")
            node.setdefault("file", f"MDDoc/{node['name']}")
            node.setdefault("summary", "")
            node.setdefault("content_hash", "")
            node.setdefault("summary_updated", None)
            node.setdefault("summary_error", "")
        return data
    
    def _ensure_doc_dir(self):
        os.makedirs(self.doc_dir, exist_ok=True)
    
    def _ensure_node_file(self, node):
        self._ensure_doc_dir()
        abs_path = rel_to_abs(node["file"])
        if not os.path.exists(abs_path):
            with open(abs_path, "w", encoding="utf-8") as handle:
                handle.write("")
    
    def _sync_existing_docs(self, data):
        self._ensure_doc_dir()
        existing_names = {}
        for node_id, node in data["nodes"].items():
            existing_names[node["name"].lower()] = node_id
    
        added = 0
        for name in os.listdir(self.doc_dir):
            if not name.lower().endswith(".md"):
                continue
            abs_path = os.path.join(self.doc_dir, name)
            if not os.path.isfile(abs_path):
                continue
            key = name.lower()
            if key in existing_names:
                node_id = existing_names[key]
                node = data["nodes"][node_id]
                expected_rel = f"MDDoc/{name}"
                if node.get("file") != expected_rel:
                    node["file"] = expected_rel
                continue
            node_id = str(uuid.uuid4())
            data["nodes"][node_id] = {
                "id": node_id,
                "name": name,
                "file": f"MDDoc/{name}",
                "summary": "",
                "content_hash": "",
                "summary_updated": None,
                "summary_error": "",
            }
            data["edges"].append({"from": data["root"], "to": node_id})
            added += 1
        return added
    
    def _load_or_init(self):
        self._ensure_doc_dir()
        if os.path.exists(self.state_path):
            with open(self.state_path, "r", encoding="utf-8") as handle:
                data = json.load(handle)
            data = self._normalize_state(data)
        else:
            data = self._default_state()
        self._sync_existing_docs(data)
        for node in data["nodes"].values():
            self._ensure_node_file(node)
        safe_write_json(self.state_path, data)
        return data
    
    def save(self):
        safe_write_json(self.state_path, self.state)
    
    def list_nodes_snapshot(self):
        with self.lock:
            return [dict(node) for node in self.state["nodes"].values()]
    
    def state_for_client(self):
        with self.lock:
            data = json.loads(json.dumps(self.state))
        for node in data["nodes"].values():
            node["file_abs"] = rel_to_abs(node["file"])
        data["state_file"] = os.path.abspath(self.state_path)
        return data
    
    def _assert_unique_name(self, name, exclude_id=None):
        for node_id, node in self.state["nodes"].items():
            if exclude_id and node_id == exclude_id:
                continue
            if node["name"].lower() == name.lower():
                raise ValueError("A node with that name already exists.")
    
    def _get_parent_id(self, node_id):
        for edge in self.state["edges"]:
            if edge["to"] == node_id:
                return edge["from"]
        return None
    
    def _build_children_map(self):
        children = {}
        for edge in self.state["edges"]:
            children.setdefault(edge["from"], []).append(edge["to"])
        return children
    
    def _is_descendant(self, node_id, possible_parent_id):
        children = self._build_children_map()
        stack = [node_id]
        seen = set()
        while stack:
            current = stack.pop()
            if current in seen:
                continue
            seen.add(current)
            for child in children.get(current, []):
                if child == possible_parent_id:
                    return True
                stack.append(child)
        return False
    
    def create_node(self, parent_id, name):
        with self.lock:
            if parent_id not in self.state["nodes"]:
                parent_id = self.state["root"]
            normalized_name = normalize_name(name)
            self._assert_unique_name(normalized_name)
            node_id = str(uuid.uuid4())
            rel_path = f"MDDoc/{normalized_name}"
            node = {
                "id": node_id,
                "name": normalized_name,
                "file": rel_path,
                "summary": "",
                "content_hash": "",
                "summary_updated": None,
                "summary_error": "",
            }
            self.state["nodes"][node_id] = node
            self.state["edges"].append({"from": parent_id, "to": node_id})
            self._ensure_node_file(node)
            self.save()
            return node_id
    
    def rename_node(self, node_id, new_name):
        with self.lock:
            node = self.state["nodes"].get(node_id)
            if not node:
                raise ValueError("Node not found.")
            normalized_name = normalize_name(new_name)
            if normalized_name.lower() == node["name"].lower():
                return
            self._assert_unique_name(normalized_name, exclude_id=node_id)
            old_abs = rel_to_abs(node["file"])
            new_rel = f"MDDoc/{normalized_name}"
            new_abs = rel_to_abs(new_rel)
            self._ensure_doc_dir()
            if os.path.exists(old_abs):
                os.replace(old_abs, new_abs)
            else:
                with open(new_abs, "w", encoding="utf-8") as handle:
                    handle.write("")
            node["name"] = normalized_name
            node["file"] = new_rel
            node["summary"] = ""
            node["summary_error"] = ""
            node["content_hash"] = ""
            node["summary_updated"] = None
            self.save()
    
    def reorder_node(self, node_id, direction):
        with self.lock:
            if node_id == self.state["root"]:
                raise ValueError("Root node cannot be reordered.")
            parent_id = self._get_parent_id(node_id)
            if not parent_id:
                raise ValueError("Parent not found.")
            edges = self.state["edges"]
            sibling_edges = [i for i, edge in enumerate(edges) if edge["from"] == parent_id]
            if not sibling_edges:
                return
            position = None
            for index, edge_index in enumerate(sibling_edges):
                if edges[edge_index]["to"] == node_id:
                    position = index
                    break
            if position is None:
                raise ValueError("Node edge not found.")
            if direction == "up":
                if position == 0:
                    return
                current_index = sibling_edges[position]
                swap_index = sibling_edges[position - 1]
            elif direction == "down":
                if position == len(sibling_edges) - 1:
                    return
                current_index = sibling_edges[position]
                swap_index = sibling_edges[position + 1]
            else:
                raise ValueError("Invalid direction.")
            edges[current_index], edges[swap_index] = edges[swap_index], edges[current_index]
            self.save()
    
    def move_node(self, node_id, new_parent_id):
        with self.lock:
            if node_id == self.state["root"]:
                raise ValueError("Root node cannot be moved.")
            if node_id not in self.state["nodes"]:
                raise ValueError("Node not found.")
            if new_parent_id not in self.state["nodes"]:
                raise ValueError("Parent not found.")
            if node_id == new_parent_id:
                raise ValueError("Cannot move node under itself.")
            if self._is_descendant(node_id, new_parent_id):
                raise ValueError("Cannot move node under its descendant.")
            edges = self.state["edges"]
            self.state["edges"] = [edge for edge in edges if edge["to"] != node_id]
            self.state["edges"].append({"from": new_parent_id, "to": node_id})
            self.save()
    
    def open_in_vscode(self, node_id):
        with self.lock:
            node = self.state["nodes"].get(node_id)
            if not node:
                raise ValueError("Node not found.")
            abs_path = rel_to_abs(node["file"])
        code_path = shutil.which("code")
        if not code_path:
            raise ValueError("VS Code CLI (code) not found in PATH.")
        subprocess.Popen([code_path, "--reuse-window", abs_path])
        return abs_path

class SummaryWorker(threading.Thread):
    def __init__(self, store):
        super().__init__(daemon=True)
        self.store = store
        self.stop_event = threading.Event()

    def run(self):
        if not OLLAMA_ENABLED:
            log("Summary worker disabled (OLLAMA_ENABLED=0).")
            return
        ollama_path = shutil.which("ollama")
        if not ollama_path:
            log("Summary worker disabled (ollama not found in PATH).")
            return
        log(f"Summary worker active (model: {OLLAMA_MODEL}).")
        while not self.stop_event.is_set():
            self._cycle(ollama_path)
            self.stop_event.wait(SUMMARY_POLL_SECONDS)
    
    def _cycle(self, ollama_path):
        nodes = self.store.list_nodes_snapshot()
        tasks = []
        for node in nodes:
            abs_path = rel_to_abs(node["file"])
            if not os.path.exists(abs_path):
                continue
            content = read_text_file(abs_path)
            content_hash = hash_text(content)
            if content_hash == node.get("content_hash", ""):
                continue
            tasks.append((node["id"], content, content_hash, abs_path))
    
        for node_id, content, content_hash, abs_path in tasks:
            summary, error = generate_summary(ollama_path, content)
            with self.store.lock:
                node = self.store.state["nodes"].get(node_id)
                if not node:
                    continue
                latest_content = read_text_file(abs_path)
                latest_hash = hash_text(latest_content)
                if latest_hash != content_hash:
                    continue
                node["content_hash"] = content_hash
                node["summary_updated"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
                if error:
                    node["summary_error"] = error
                else:
                    node["summary"] = summary
                    node["summary_error"] = ""
                self.store.save()

def generate_summary(ollama_path, content):
    if not content.strip():
        return "", ""
    prompt = f"{SUMMARY_PROMPT}{content}"
    try:
        result = subprocess.run(
            [ollama_path, "run", OLLAMA_MODEL],
            input=prompt,
            text=True,
            capture_output=True,
            timeout=SUMMARY_TIMEOUT_SECONDS,
            encoding="utf-8",
            errors="replace",
        )
    except subprocess.TimeoutExpired:
        return "", "Summary timed out."
    except OSError as exc:
        return "", f"Summary error: {exc}"
    if result.returncode != 0:
        return "", (result.stderr.strip() or "Summary failed.")
    return result.stdout.strip(), ""

class RequestHandler(BaseHTTPRequestHandler):
    server_version = "MMMarkDown/0.1"

    def log_message(self, format_string, *args):
        return
    
    def _read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length)
        try:
            return json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise ValueError(f"Invalid JSON: {exc}")
    
    def _send_json(self, payload, status=HTTPStatus.OK):
        raw = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)
    
    def _send_error_json(self, message, status=HTTPStatus.BAD_REQUEST):
        self._send_json({"ok": False, "error": message}, status=status)
    
    def _serve_static(self, path):
        if path == "/":
            path = "/index.html"
        safe_root = os.path.abspath(STATIC_DIR)
        safe_path = os.path.abspath(os.path.join(safe_root, path.lstrip("/")))
        if not safe_path.startswith(safe_root):
            self.send_error(HTTPStatus.FORBIDDEN)
            return
        if os.path.isdir(safe_path):
            safe_path = os.path.join(safe_path, "index.html")
        if not os.path.exists(safe_path):
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        content_type, _ = mimetypes.guess_type(safe_path)
        content_type = content_type or "application/octet-stream"
        with open(safe_path, "rb") as handle:
            data = handle.read()
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        if safe_path.endswith(".html"):
            self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)
    
    def do_GET(self):
        parsed = urlparse(self.path)
        if parsed.path == "/api/state":
            payload = store.state_for_client()
            payload["ok"] = True
            self._send_json(payload)
            return
        self._serve_static(parsed.path)
    
    def do_POST(self):
        parsed = urlparse(self.path)
        try:
            if parsed.path == "/api/node/create":
                payload = self._read_json()
                node_id = store.create_node(payload.get("parent_id"), payload.get("name", ""))
                self._send_json({"ok": True, "node_id": node_id, "state": store.state_for_client()})
                return
            if parsed.path == "/api/node/rename":
                payload = self._read_json()
                store.rename_node(payload.get("node_id"), payload.get("name", ""))
                self._send_json({"ok": True, "state": store.state_for_client()})
                return
            if parsed.path == "/api/node/reorder":
                payload = self._read_json()
                store.reorder_node(payload.get("node_id"), payload.get("direction"))
                self._send_json({"ok": True, "state": store.state_for_client()})
                return
            if parsed.path == "/api/node/move":
                payload = self._read_json()
                store.move_node(payload.get("node_id"), payload.get("new_parent_id"))
                self._send_json({"ok": True, "state": store.state_for_client()})
                return
            if parsed.path == "/api/node/open":
                payload = self._read_json()
                path = store.open_in_vscode(payload.get("node_id"))
                self._send_json({"ok": True, "path": path})
                return
        except ValueError as exc:
            self._send_error_json(str(exc), status=HTTPStatus.BAD_REQUEST)
            return
        except Exception as exc:
            self._send_error_json(str(exc), status=HTTPStatus.INTERNAL_SERVER_ERROR)
            return
        self.send_error(HTTPStatus.NOT_FOUND)

store = None

def iter_watch_files():
    watch_files = []
    for root, dirs, files in os.walk(BASE_DIR):
        dirs[:] = [d for d in dirs if d not in (".git", "__pycache__")]
        for name in files:
            if name.endswith(".py"):
                watch_files.append(os.path.join(root, name))
    if not watch_files:
        watch_files.append(os.path.abspath(__file__))
    return watch_files

def latest_mtime(paths):
    newest = 0.0
    for path in paths:
        try:
            newest = max(newest, os.path.getmtime(path))
        except OSError:
            continue
    return newest

def run_with_reloader(argv):
    watch_files = iter_watch_files()
    last_mtime = latest_mtime(watch_files)
    log("Auto-reload enabled. Watching for code changes.")
    while True:
        env = os.environ.copy()
        env["MMM_RUN_MAIN"] = "1"
        process = subprocess.Popen([sys.executable] + argv, env=env)
        try:
            while True:
                time.sleep(0.5)
                if process.poll() is not None:
                    return process.returncode
                watch_files = iter_watch_files()
                current_mtime = latest_mtime(watch_files)
                if current_mtime > last_mtime:
                    last_mtime = current_mtime
                    log("Change detected. Reloading...")
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                    break
        except KeyboardInterrupt:
            process.terminate()
            return 0

def run_server(args):
    global store
    store = MapStore(args.state, DOC_DIR)

    worker = SummaryWorker(store)
    worker.start()
    
    server = ThreadingHTTPServer((args.host, args.port), RequestHandler)
    log(f"Serving on http://{args.host}:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("Shutting down.")
        server.shutdown()

def main():
    parser = argparse.ArgumentParser(description="MMMarkDown local mind map server")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8000, help="Port to bind (default: 8000)")
    parser.add_argument("--state", default=DEFAULT_STATE_FILE, help="Path to .mmm state file")
    parser.add_argument("--no-reload", action="store_true", help="Disable auto reload on code changes")
    args = parser.parse_args()

    reload_enabled = RELOAD_ENABLED and not args.no_reload
    if reload_enabled and os.environ.get("MMM_RUN_MAIN") != "1":
        return_code = run_with_reloader(sys.argv)
        raise SystemExit(return_code)
    
    run_server(args)

if __name__ == "__main__":
    main()
