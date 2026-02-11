import json
import os
from pathlib import Path

CONFIG_FILE = Path.home() / ".config" / "alien_file_manager" / "shortcuts.json"

DEFAULT_SHORTCUTS = [
    {"name": "홈", "path": str(Path.home())},
    {"name": "다운로드", "path": str(Path.home() / "Downloads")},
    {"name": "문서", "path": str(Path.home() / "Documents")},
]

class ConfigManager:
    def __init__(self):
        self.ensure_config_dir()
        self.shortcuts = self.load_shortcuts()

    def ensure_config_dir(self):
        if not CONFIG_FILE.parent.exists():
            CONFIG_FILE.parent.mkdir(parents=True, exist_ok=True)

    def load_shortcuts(self):
        if not CONFIG_FILE.exists():
            return DEFAULT_SHORTCUTS
        
        try:
            with open(CONFIG_FILE, "r", encoding="utf-8") as f:
                return json.load(f)
        except (json.JSONDecodeError, OSError):
            return DEFAULT_SHORTCUTS

    def save_shortcuts(self, shortcuts):
        self.shortcuts = shortcuts
        try:
            with open(CONFIG_FILE, "w", encoding="utf-8") as f:
                json.dump(shortcuts, f, indent=4)
        except OSError as e:
            print(f"Error saving shortcuts: {e}")

    def add_shortcut(self, name, path):
        self.shortcuts.append({"name": name, "path": path})
        self.save_shortcuts(self.shortcuts)

    def remove_shortcut(self, index):
        if 0 <= index < len(self.shortcuts):
            self.shortcuts.pop(index)
            self.save_shortcuts(self.shortcuts)
