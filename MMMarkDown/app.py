"""MMMarkDown local mind map server.

This module hosts a small HTTP server that serves the UI, persists a map state
(.mmm JSON), keeps Markdown files in sync, and optionally generates summaries
using a local Ollama model. It also provides a simple file-change reloader.
"""

import argparse
import hashlib
import json
import mimetypes
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import uuid
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlparse

# ---- Configuration ----
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_STATE_FILE = os.environ.get("MMM_FILE", os.path.join(BASE_DIR, "mindmap.mmm"))
DOC_DIR = os.environ.get("MMM_DOC_DIR", os.path.join(BASE_DIR, "MDDoc"))
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
MARKTEXT_EXE = os.path.join(
    os.path.expanduser("~"),
    "AppData",
    "Local",
    "Programs",
    "MarkText",
    "MarkText.exe",
)

EDITOR_PRESETS = {
    "vscode": {"label": "VS Code", "command": ["code", "--reuse-window"]},
    "typora": {"label": "Typora", "command": ["typora"]},
    "marktext": {"label": "MarkText", "command": [MARKTEXT_EXE]},
    "obsidian": {"label": "Obsidian", "command": ["obsidian"]},
    "notepad++": {"label": "Notepad++", "command": ["notepad++.exe"]},
    "notepad": {"label": "Notepad", "command": ["notepad.exe"]},
}

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

# ---- Utilities ----
def log(message):
    """Lightweight console logger with a consistent prefix."""
    print(f"[MMM] {message}")

def rel_to_abs(rel_path):
    """Resolve a repo-relative path to an absolute path."""
    if os.path.isabs(rel_path):
        return os.path.abspath(rel_path)
    parts = rel_path.replace("\\", "/").split("/")
    if parts and parts[0].lower() == "mddoc":
        return os.path.abspath(os.path.join(DOC_DIR, *parts[1:]))
    return os.path.abspath(os.path.join(BASE_DIR, *parts))


def resolve_state_path(state_path):
    """Resolve the state file path and its workspace directory."""
    abs_state = os.path.abspath(state_path)
    workspace = os.path.dirname(abs_state)
    return abs_state, workspace

def read_text_file(path):
    """Read text files with BOM-aware UTF-8 fallbacking to CP949."""
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            return handle.read()
    except FileNotFoundError:
        return ""
    except UnicodeDecodeError:
        with open(path, "r", encoding="cp949", errors="replace") as handle:
            return handle.read()

def hash_text(text):
    """Return a stable SHA-256 hash for change detection."""
    return hashlib.sha256(text.encode("utf-8", errors="replace")).hexdigest()

def is_summary_candidate(content):
    """Return False for empty docs or docs with only headings."""
    lines = [line.strip() for line in content.splitlines() if line.strip()]
    if not lines:
        return False
    if all(line.lstrip().startswith("#") for line in lines):
        return False
    return True

def retry_io(action, context="", attempts=6, base_delay=0.2):
    """Retry transient I/O errors (e.g. sync lock) with backoff."""
    last_error = None
    for index in range(attempts):
        try:
            return action()
        except (OSError, PermissionError) as exc:
            last_error = exc
            delay = base_delay * (index + 1)
            time.sleep(delay)
    if context:
        log(f"I/O retry failed: {context}: {last_error}")
    raise last_error

def safe_write_json(path, payload):
    """Atomically write JSON with UTF-8 BOM to avoid partial files."""
    dir_name = os.path.dirname(path)
    if dir_name:
        os.makedirs(dir_name, exist_ok=True)
    tmp_path = f"{path}.tmp"
    def _write():
        with open(tmp_path, "w", encoding="utf-8-sig") as handle:
            json.dump(payload, handle, indent=2, ensure_ascii=False)
        os.replace(tmp_path, path)
    retry_io(_write, context=f"write state {path}")

def write_text_file(path, text):
    """Write text files using UTF-8 BOM, creating directories if needed."""
    dir_name = os.path.dirname(path)
    if dir_name:
        os.makedirs(dir_name, exist_ok=True)
    def _write():
        with open(path, "w", encoding="utf-8-sig") as handle:
            handle.write(text)
    retry_io(_write, context=f"write text {path}")

# ---- Markdown image localization ----
IMAGE_MD_RE = re.compile(r"!\[([^\]]*)\]\(([^)]+)\)")
IMAGE_HTML_RE = re.compile(r'(<img\s+[^>]*?src=["\'])([^"\']+)(["\'])', re.IGNORECASE)

def _split_md_link_target(text):
    text = text.strip()
    if text.startswith("<"):
        end = text.find(">")
        if end != -1:
            return text[1:end], text[end + 1 :].strip()
    in_quote = None
    for index, char in enumerate(text):
        if char in ("'", '"'):
            if in_quote == char:
                in_quote = None
            elif in_quote is None:
                in_quote = char
        elif char.isspace() and in_quote is None:
            return text[:index], text[index:].strip()
    return text, ""

def _is_external_link(url):
    if not url:
        return True
    lower = url.lower()
    if lower.startswith(("http://", "https://", "data:", "mailto:")):
        return True
    parsed = urlparse(url)
    return parsed.scheme in ("http", "https", "data", "mailto")

def _resolve_local_path(doc_dir, url):
    clean_url = url.split("#")[0].split("?")[0]
    clean_url = unquote(clean_url)
    if re.match(r"^[a-zA-Z]:[\\/]", clean_url) or clean_url.startswith("\\\\"):
        return os.path.abspath(clean_url)
    if os.path.isabs(clean_url):
        return os.path.abspath(clean_url)
    return os.path.abspath(os.path.join(doc_dir, clean_url))

def _path_within(path, root):
    root_abs = os.path.abspath(root)
    try:
        return os.path.commonpath([os.path.abspath(path), root_abs]) == root_abs
    except ValueError:
        return False

def _unique_destination(target_dir, filename):
    base, ext = os.path.splitext(filename)
    candidate = os.path.join(target_dir, filename)
    if not os.path.exists(candidate):
        return candidate
    index = 1
    while True:
        candidate = os.path.join(target_dir, f"{base}_{index}{ext}")
        if not os.path.exists(candidate):
            return candidate
        index += 1

def localize_markdown_images(abs_path, content):
    """Copy external local images into the doc folder and rewrite links."""
    doc_dir = os.path.dirname(abs_path)
    base_name = os.path.splitext(os.path.basename(abs_path))[0]
    changed = False

    def copy_local_image(url):
        nonlocal changed
        if not url or _is_external_link(url) or url.startswith("#"):
            return None
        resolved = _resolve_local_path(doc_dir, url)
        if not resolved or not os.path.isfile(resolved):
            return None
        if _path_within(resolved, doc_dir):
            return None
        target_dir = os.path.join(doc_dir, "Images", base_name)
        os.makedirs(target_dir, exist_ok=True)
        dest_path = _unique_destination(target_dir, os.path.basename(resolved))
        try:
            shutil.copy2(resolved, dest_path)
        except OSError:
            return None
        changed = True
        return f"./Images/{base_name}/{os.path.basename(dest_path)}"

    def replace_md(match):
        alt_text = match.group(1)
        inner = match.group(2)
        url, title = _split_md_link_target(inner)
        rel_path = copy_local_image(url)
        if not rel_path:
            return match.group(0)
        new_inner = f"{rel_path} {title}".strip() if title else rel_path
        return f"![{alt_text}]({new_inner})"

    def replace_html(match):
        prefix, url, suffix = match.groups()
        rel_path = copy_local_image(url)
        if not rel_path:
            return match.group(0)
        return f"{prefix}{rel_path}{suffix}"

    updated = IMAGE_MD_RE.sub(replace_md, content)
    updated = IMAGE_HTML_RE.sub(replace_html, updated)
    return updated, changed

def normalize_name(raw_name):
    """Validate and normalize a node name into a safe .md filename."""
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

# ---- State store ----
class MapStore:
    def __init__(self, state_path, doc_dir):
        self.state_path = state_path
        self.doc_dir = doc_dir
        self.lock = threading.Lock()
        self.state = self._load_or_init()

    def _build_node(self, node_id, name, file_rel):
        return {
            "id": node_id,
            "name": name,
            "file": file_rel,
            "summary": "",
            "content_hash": "",
            "summary_updated": None,
            "summary_error": "",
            "collapsed": False,
        }

    def _apply_node_defaults(self, node_id, node):
        name = node.get("name") or f"{node_id}.md"
        file_rel = node.get("file") or f"MDDoc/{name}"
        defaults = self._build_node(node_id, name, file_rel)
        for key, value in defaults.items():
            node.setdefault(key, value)
        node["id"] = node_id
        return node

    def _clear_summary_state(self, node, clear_summary=False):
        node["content_hash"] = ""
        node["summary_error"] = ""
        node["summary_updated"] = None
        if clear_summary:
            node["summary"] = ""

    def _default_state(self):
        root_id = str(uuid.uuid4())
        root_name = "Root.md"
        state = {
            "version": 1,
            "root": root_id,
            "nodes": {
                root_id: self._build_node(root_id, root_name, f"MDDoc/{root_name}")
            },
            "settings": {"editor": "vscode", "custom_editor": ""},
            "edges": [],
        }
        return state

    def _normalize_state(self, data):
        if not isinstance(data, dict):
            return self._default_state()
        data.setdefault("version", 1)
        data.setdefault("nodes", {})
        data.setdefault("edges", [])
        settings = data.get("settings")
        if not isinstance(settings, dict):
            settings = {}
            data["settings"] = settings
        settings.setdefault("editor", "vscode")
        settings.setdefault("custom_editor", "")
        if not data.get("nodes"):
            data = self._default_state()
        if "root" not in data or data["root"] not in data["nodes"]:
            data["root"] = next(iter(data["nodes"].keys()))
        for node_id, node in list(data["nodes"].items()):
            self._apply_node_defaults(node_id, node)
        return data

    def _ensure_doc_dir(self):
        os.makedirs(self.doc_dir, exist_ok=True)

    def _normalize_rel_path(self, *parts):
        """Normalize path segments to use forward slashes in state files."""
        cleaned = [part.strip("/\\") for part in parts if part]
        return "/".join(cleaned)

    def _resolve_parent_for_path(self, existing_names, root_id, rel_path):
        parent_folder = os.path.dirname(rel_path).replace("\\", "/")
        if not parent_folder:
            return root_id
        parent_name = f"{os.path.basename(parent_folder)}.md"
        return existing_names.get(parent_name.lower(), root_id)

    def _folder_parts_for_parent(self, parent_id):
        parts = []
        current_id = parent_id
        while current_id and current_id != self.state["root"]:
            node = self.state["nodes"].get(current_id)
            if not node:
                break
            base = os.path.splitext(node["name"])[0].strip()
            if base:
                parts.append(base)
            current_id = self._get_parent_id(current_id)
        return list(reversed(parts))

    def _expected_rel_path_for_node(self, node_id):
        node = self.state["nodes"][node_id]
        parent_id = self._get_parent_id(node_id)
        parts = ["MDDoc"] + self._folder_parts_for_parent(parent_id) + [node["name"]]
        return self._normalize_rel_path(*parts)

    def _initial_node_content(self, name):
        """Return starter content for a newly created Markdown file."""
        base_name = os.path.splitext(name)[0]
        return f"# {base_name}\n\n"

    def _ensure_node_file(self, node, initial_content=None):
        """Ensure the node's Markdown file exists on disk."""
        self._ensure_doc_dir()
        abs_path = rel_to_abs(node["file"])
        if not os.path.exists(abs_path):
            content = initial_content if initial_content is not None else ""
            write_text_file(abs_path, content)

    def _safe_delete_file(self, rel_path):
        abs_path = os.path.abspath(rel_to_abs(rel_path))
        doc_root = os.path.abspath(self.doc_dir)
        if not abs_path.startswith(doc_root):
            return
        try:
            os.remove(abs_path)
        except FileNotFoundError:
            return
        except OSError:
            return

    def _collect_subtree_ids(self, node_id):
        children = self._build_children_map()
        to_visit = [node_id]
        result = []
        seen = set()
        while to_visit:
            current = to_visit.pop()
            if current in seen:
                continue
            seen.add(current)
            result.append(current)
            for child_id in children.get(current, []):
                to_visit.append(child_id)
        return result

    def _sync_node_paths(self, node_ids):
        for target_id in node_ids:
            node = self.state["nodes"].get(target_id)
            if not node:
                continue
            expected_rel = self._expected_rel_path_for_node(target_id)
            if node["file"] == expected_rel:
                continue
            old_abs = rel_to_abs(node["file"])
            new_abs = rel_to_abs(expected_rel)
            new_dir = os.path.dirname(new_abs)
            if new_dir:
                os.makedirs(new_dir, exist_ok=True)
            if os.path.exists(old_abs):
                def _move():
                    os.replace(old_abs, new_abs)
                retry_io(_move, context=f"move file {old_abs} -> {new_abs}")
            else:
                write_text_file(new_abs, self._initial_node_content(node["name"]))
            node["file"] = expected_rel

    def _sync_existing_docs(self, data):
        # Build a case-insensitive lookup of existing node names to avoid duplicates.
        self._ensure_doc_dir()
        existing_names = {}
        for node_id, node in data["nodes"].items():
            existing_names[node["name"].lower()] = node_id

        added = 0
        for root, _, files in os.walk(self.doc_dir):
            for name in files:
                if not name.lower().endswith(".md"):
                    continue
                abs_path = os.path.join(root, name)
                if not os.path.isfile(abs_path):
                    continue
                rel_path = os.path.relpath(abs_path, self.doc_dir).replace("\\", "/")
                expected_rel = self._normalize_rel_path("MDDoc", rel_path)
                key = name.lower()
                existing_id = existing_names.get(key)
                if existing_id:
                    node = data["nodes"][existing_id]
                    if node.get("file") != expected_rel:
                        node["file"] = expected_rel
                    continue
                parent_id = self._resolve_parent_for_path(existing_names, data["root"], rel_path)
                node_id = str(uuid.uuid4())
                data["nodes"][node_id] = self._build_node(node_id, name, expected_rel)
                data["edges"].append({"from": parent_id, "to": node_id})
                existing_names[key] = node_id
                added += 1
        return added

    def _load_or_init(self):
        self._ensure_doc_dir()
        if os.path.exists(self.state_path):
            with open(self.state_path, "r", encoding="utf-8-sig") as handle:
                data = json.load(handle)
            data = self._normalize_state(data)
        else:
            data = self._default_state()
        self._sync_existing_docs(data)
        for node in data["nodes"].values():
            self._ensure_node_file(node, initial_content=self._initial_node_content(node["name"]))
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

    def create_node(self, parent_id, name, insert_after_id=None):
        with self.lock:
            if parent_id not in self.state["nodes"]:
                parent_id = self.state["root"]
            normalized_name = normalize_name(name)
            self._assert_unique_name(normalized_name)
            node_id = str(uuid.uuid4())
            node = self._build_node(node_id, normalized_name, "")
            self.state["nodes"][node_id] = node
            new_edge = {"from": parent_id, "to": node_id}
            if insert_after_id:
                edges = self.state["edges"]
                sibling_edges = [i for i, edge in enumerate(edges) if edge["from"] == parent_id]
                insert_index = None
                for index, edge_index in enumerate(sibling_edges):
                    if edges[edge_index]["to"] == insert_after_id:
                        insert_index = edge_index + 1
                        break
                if insert_index is None:
                    edges.append(new_edge)
                else:
                    edges.insert(insert_index, new_edge)
            else:
                self.state["edges"].append(new_edge)
            node["file"] = self._expected_rel_path_for_node(node_id)
            self._ensure_node_file(node, initial_content=self._initial_node_content(normalized_name))
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
            node["name"] = normalized_name
            self._clear_summary_state(node, clear_summary=True)
            self._sync_node_paths(self._collect_subtree_ids(node_id))
            self.save()

    def reorder_node(self, node_id, direction):
        # Only siblings under the same parent are reordered by swapping edge positions.
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
        # Prevent cycles by disallowing moves under descendants.
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
            self._sync_node_paths(self._collect_subtree_ids(node_id))
            self.save()

    def delete_node(self, node_id):
        # Collect the full subtree so both nodes and files are removed together.
        with self.lock:
            if node_id == self.state["root"]:
                raise ValueError("Root node cannot be deleted.")
            if node_id not in self.state["nodes"]:
                raise ValueError("Node not found.")
            to_delete = set(self._collect_subtree_ids(node_id))
            self.state["edges"] = [
                edge
                for edge in self.state["edges"]
                if edge["from"] not in to_delete and edge["to"] not in to_delete
            ]
            for target_id in to_delete:
                node = self.state["nodes"].pop(target_id, None)
                if node:
                    self._safe_delete_file(node.get("file", ""))
            self.save()

    def force_resummarize(self):
        """Mark all nodes to be re-summarized on the next worker cycle."""
        with self.lock:
            for node in self.state["nodes"].values():
                self._clear_summary_state(node)
            self.save()

    def _assert_editor_exists(self, command):
        if os.path.isabs(command) or os.path.dirname(command):
            if not os.path.exists(command):
                raise ValueError(f"Editor not found: {command}")
            return
        if not shutil.which(command):
            raise ValueError(f"Editor not found in PATH: {command}")

    def _get_editor_args(self, settings, abs_path):
        editor_key = (settings.get("editor") or "vscode").lower()
        if editor_key not in EDITOR_PRESETS:
            raise ValueError("Unknown editor.")
        preset = EDITOR_PRESETS[editor_key]["command"]
        self._assert_editor_exists(preset[0])
        return preset + [abs_path]

    def update_editor_settings(self, editor, custom_editor):
        editor_value = (editor or "").strip().lower()
        if not editor_value:
            editor_value = "vscode"
        if editor_value not in EDITOR_PRESETS:
            raise ValueError("Unknown editor.")
        with self.lock:
            settings = self.state.setdefault("settings", {})
            settings["editor"] = editor_value
            if "custom_editor" in settings:
                settings["custom_editor"] = ""
            self.save()

    def set_node_collapsed(self, node_id, collapsed):
        """Update the collapsed state for a node."""
        with self.lock:
            node = self.state["nodes"].get(node_id)
            if not node:
                raise ValueError("Node not found.")
            node["collapsed"] = bool(collapsed)
            self.save()

    def realign_folders(self):
        """Align all files to match the current node hierarchy."""
        with self.lock:
            self._sync_node_paths(list(self.state["nodes"].keys()))
            self.save()

    def open_in_editor(self, node_id):
        with self.lock:
            node = self.state["nodes"].get(node_id)
            if not node:
                raise ValueError("Node not found.")
            settings = dict(self.state.get("settings") or {})
            abs_path = rel_to_abs(node["file"])
        args = self._get_editor_args(settings, abs_path)
        subprocess.Popen(args)
        return abs_path

# ---- Summary worker ----
class SummaryWorker(threading.Thread):
    """Background thread that localizes images and refreshes summaries."""
    def __init__(self, store):
        super().__init__(daemon=True)
        self.store = store
        self.stop_event = threading.Event()
        self.ollama_path = None
        self.summary_enabled = OLLAMA_ENABLED
        self.last_ollama_check = 0.0

    def run(self):
        self.ollama_path = shutil.which("ollama")
        self.last_ollama_check = time.time()
        if not self.summary_enabled:
            log("File worker active (summaries disabled).")
        elif not self.ollama_path:
            log("File worker active (ollama not found in PATH).")
        else:
            log(f"Summary worker active (model: {OLLAMA_MODEL}).")
        while not self.stop_event.is_set():
            try:
                self._refresh_ollama_path()
                self._cycle()
            except Exception as exc:
                log(f"Summary worker error: {exc}")
            self.stop_event.wait(SUMMARY_POLL_SECONDS)

    def _refresh_ollama_path(self):
        if not self.summary_enabled:
            return
        now = time.time()
        if now - self.last_ollama_check < 10:
            return
        self.last_ollama_check = now
        path = shutil.which("ollama")
        if path and not self.ollama_path:
            log(f"Summary worker active (model: {OLLAMA_MODEL}).")
        if not path and self.ollama_path:
            log("File worker active (ollama not found in PATH).")
        self.ollama_path = path

    def _cycle(self):
        # Scan all nodes, update content hashes, and refresh summaries when needed.
        nodes = self.store.list_nodes_snapshot()
        for node in nodes:
            abs_path = rel_to_abs(node["file"])
            if not os.path.exists(abs_path):
                continue
            content = read_text_file(abs_path)
            content_hash = hash_text(content)
            if content_hash == node.get("content_hash", ""):
                continue
            updated_content, updated = localize_markdown_images(abs_path, content)
            if updated:
                write_text_file(abs_path, updated_content)
                content = updated_content
                content_hash = hash_text(content)
            summary = ""
            error = ""
            summary_candidate = is_summary_candidate(content)
            should_summarize = summary_candidate and self.summary_enabled and self.ollama_path
            if should_summarize:
                summary, error = generate_summary(self.ollama_path, content)
            with self.store.lock:
                node = self.store.state["nodes"].get(node["id"])
                if not node:
                    continue
                latest_content = read_text_file(abs_path)
                latest_hash = hash_text(latest_content)
                if latest_hash != content_hash:
                    continue
                node["content_hash"] = content_hash
                if summary_candidate:
                    if should_summarize:
                        node["summary_updated"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
                        if error:
                            node["summary_error"] = error
                        else:
                            node["summary"] = summary
                            node["summary_error"] = ""
                else:
                    node["summary"] = ""
                    node["summary_error"] = ""
                    node["summary_updated"] = None
                self.store.save()

def generate_summary(ollama_path, content):
    """Run Ollama to summarize content and return (summary, error)."""
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

# ---- HTTP server ----
class RequestHandler(BaseHTTPRequestHandler):
    """HTTP handler for static files and JSON API endpoints."""
    server_version = "MMMarkDown/0.1"
    POST_ROUTES = {
        "/api/node/create": ("_handle_node_create", True),
        "/api/node/rename": ("_handle_node_rename", True),
        "/api/node/reorder": ("_handle_node_reorder", True),
        "/api/node/move": ("_handle_node_move", True),
        "/api/node/delete": ("_handle_node_delete", True),
        "/api/node/collapse": ("_handle_node_collapse", True),
        "/api/folders/realign": ("_handle_realign_folders", True),
        "/api/summary/refresh": ("_handle_refresh_summaries", True),
        "/api/settings/editor": ("_handle_update_editor", True),
        "/api/node/open": ("_handle_node_open", False),
    }

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

    def _handle_node_create(self, payload):
        node_id = store.create_node(
            payload.get("parent_id"),
            payload.get("name", ""),
            payload.get("insert_after_id"),
        )
        return {"node_id": node_id}

    def _handle_node_rename(self, payload):
        store.rename_node(payload.get("node_id"), payload.get("name", ""))
        return {}

    def _handle_node_reorder(self, payload):
        store.reorder_node(payload.get("node_id"), payload.get("direction"))
        return {}

    def _handle_node_move(self, payload):
        store.move_node(payload.get("node_id"), payload.get("new_parent_id"))
        return {}

    def _handle_node_delete(self, payload):
        store.delete_node(payload.get("node_id"))
        return {}

    def _handle_node_collapse(self, payload):
        store.set_node_collapsed(payload.get("node_id"), payload.get("collapsed"))
        return {}

    def _handle_realign_folders(self, payload):
        store.realign_folders()
        return {}

    def _handle_refresh_summaries(self, payload):
        store.force_resummarize()
        return {}

    def _handle_update_editor(self, payload):
        store.update_editor_settings(payload.get("editor"), payload.get("custom_editor"))
        return {}

    def _handle_node_open(self, payload):
        path = store.open_in_editor(payload.get("node_id"))
        return {"path": path}

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
        # Route API calls and return JSON responses with consistent error handling.
        parsed = urlparse(self.path)
        try:
            route = self.POST_ROUTES.get(parsed.path)
            if not route:
                self.send_error(HTTPStatus.NOT_FOUND)
                return
            handler_name, include_state = route
            payload = self._read_json()
            handler = getattr(self, handler_name)
            response = handler(payload) or {}
            if include_state:
                response["state"] = store.state_for_client()
            response["ok"] = True
            self._send_json(response)
            return
        except ValueError as exc:
            self._send_error_json(str(exc), status=HTTPStatus.BAD_REQUEST)
            return
        except Exception as exc:
            self._send_error_json(str(exc), status=HTTPStatus.INTERNAL_SERVER_ERROR)
            return

store = None

# ---- Auto reload ----
def iter_watch_files():
    """Collect Python files to watch for hot reload."""
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
    """Return the newest modification time among the given paths."""
    newest = 0.0
    for path in paths:
        try:
            newest = max(newest, os.path.getmtime(path))
        except OSError:
            continue
    return newest

def run_with_reloader(argv):
    """Start the server and restart it when code changes are detected."""
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

# ---- Entry point ----
def run_server(args):
    """Initialize state, background workers, and the HTTP server."""
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
    """CLI entry point for running the server with optional reload."""
    parser = argparse.ArgumentParser(description="MMMarkDown local mind map server")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8000, help="Port to bind (default: 8000)")
    parser.add_argument("--state", default=DEFAULT_STATE_FILE, help="Path to .mmm state file")
    parser.add_argument("--no-reload", action="store_true", help="Disable auto reload on code changes")
    args = parser.parse_args()

    state_path, workspace_dir = resolve_state_path(args.state)
    os.makedirs(workspace_dir, exist_ok=True)
    args.state = state_path
    global DOC_DIR
    DOC_DIR = os.path.join(workspace_dir, "MDDoc")

    reload_enabled = RELOAD_ENABLED and not args.no_reload
    if reload_enabled and os.environ.get("MMM_RUN_MAIN") != "1":
        return_code = run_with_reloader(sys.argv)
        raise SystemExit(return_code)

    run_server(args)

if __name__ == "__main__":
    main()
