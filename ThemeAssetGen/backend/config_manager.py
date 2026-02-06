import json
import os
from pathlib import Path
from backend.default_config import DEFAULT_CONFIG

class ConfigManager:
    def __init__(self, config_file="config.json"):
        self.base_dir = Path(__file__).resolve().parent.parent
        self.config_path = self.base_dir / config_file
        self.config = {}
        self.load_config()

    def load_config(self):
        """Load configuration from file or create with defaults"""
        if self.config_path.exists():
            try:
                with open(self.config_path, 'r', encoding='utf-8') as f:
                    self.config = json.load(f)
                    
                # Merge with defaults for any missing keys
                self._merge_defaults()
            except Exception as e:
                print(f"Error loading config.json, using defaults: {e}")
                self.config = DEFAULT_CONFIG.copy()
        else:
            self.config = DEFAULT_CONFIG.copy()
            self.save_config()
            
        # Resolve Dynamic Paths if not set or if they are None
        if not self.config.get("COMFYUI_WORKFLOW_PATH"):
            self.config["COMFYUI_WORKFLOW_PATH"] = str(self.base_dir / "backend" / "comfyuiapi" / "zit_assetgen_api.json")

    def _merge_defaults(self):
        """Ensure all default keys exist in loaded config"""
        modified = False
        for key, value in DEFAULT_CONFIG.items():
            if key not in self.config:
                self.config[key] = value
                modified = True
            elif isinstance(value, dict) and isinstance(self.config[key], dict):
                # Nested merge for ASSET_GENERATION_COUNTS
                for sub_key, sub_val in value.items():
                    if sub_key not in self.config[key]:
                        self.config[key][sub_key] = sub_val
                        modified = True
                        
        if modified:
            self.save_config()

    def save_config(self):
        """Save current configuration to file"""
        try:
            with open(self.config_path, 'w', encoding='utf-8') as f:
                json.dump(self.config, f, indent=4, ensure_ascii=False)
        except Exception as e:
            print(f"Error saving config.json: {e}")

    def get(self, key, default=None):
        return self.config.get(key, default)

    def set(self, key, value):
        self.config[key] = value
        self.save_config()

    def update_asset_count(self, category, count):
        if "ASSET_GENERATION_COUNTS" not in self.config:
            self.config["ASSET_GENERATION_COUNTS"] = {}
        self.config["ASSET_GENERATION_COUNTS"][category] = count
        self.save_config()

# Global instance
config_manager = ConfigManager()
