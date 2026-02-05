#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Config Editor GUI - Edit ThemeAssetGen configuration visually
"""

import tkinter as tk
from tkinter import ttk, messagebox
import os
import sys

# Add project root to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

CONFIG_FILE = os.path.join(os.path.dirname(__file__), "backend", "config.py")


class ConfigEditor:
    def __init__(self, root):
        self.root = root
        self.root.title("ThemeAssetGen Config Editor")
        self.root.geometry("700x650")
        self.root.resizable(True, True)
        
        # Dark theme colors
        self.bg_color = "#1e1e2e"
        self.fg_color = "#cdd6f4"
        self.accent_color = "#89b4fa"
        self.entry_bg = "#313244"
        self.button_bg = "#45475a"
        self.success_color = "#a6e3a1"
        self.warning_color = "#f9e2af"
        
        self.root.configure(bg=self.bg_color)
        
        # Configure styles
        self.style = ttk.Style()
        self.style.theme_use('clam')
        self.configure_styles()
        
        # Variables to store config values
        self.config_vars = {}
        
        # Load current config
        self.load_config()
        
        # Create UI
        self.create_ui()
        
    def configure_styles(self):
        self.style.configure("TFrame", background=self.bg_color)
        self.style.configure("TLabel", background=self.bg_color, foreground=self.fg_color, font=("Segoe UI", 10))
        self.style.configure("Header.TLabel", background=self.bg_color, foreground=self.accent_color, font=("Segoe UI", 12, "bold"))
        self.style.configure("TEntry", fieldbackground=self.entry_bg, foreground=self.fg_color)
        self.style.configure("TButton", background=self.button_bg, foreground=self.fg_color, font=("Segoe UI", 10))
        self.style.configure("TSpinbox", fieldbackground=self.entry_bg, foreground=self.fg_color)
        self.style.configure("TLabelframe", background=self.bg_color, foreground=self.accent_color)
        self.style.configure("TLabelframe.Label", background=self.bg_color, foreground=self.accent_color, font=("Segoe UI", 11, "bold"))
        
    def load_config(self):
        """Load current configuration from config.py"""
        try:
            from backend import config
            
            # Service URLs
            self.config_vars["OLLAMA_URL"] = tk.StringVar(value=config.OLLAMA_URL)
            self.config_vars["OLLAMA_MODEL"] = tk.StringVar(value=config.OLLAMA_MODEL)
            self.config_vars["COMFYUI_URL"] = tk.StringVar(value=config.COMFYUI_URL)
            self.config_vars["HUNYUAN3D_URL"] = tk.StringVar(value=config.HUNYUAN3D_URL)
            
            # Timeouts
            self.config_vars["OLLAMA_TIMEOUT"] = tk.IntVar(value=config.OLLAMA_TIMEOUT)
            self.config_vars["COMFYUI_TIMEOUT"] = tk.IntVar(value=config.COMFYUI_TIMEOUT)
            self.config_vars["HUNYUAN3D_TIMEOUT"] = tk.IntVar(value=config.HUNYUAN3D_TIMEOUT)
            
            # Server
            self.config_vars["SERVER_HOST"] = tk.StringVar(value=config.SERVER_HOST)
            self.config_vars["SERVER_PORT"] = tk.IntVar(value=config.SERVER_PORT)
            
            # Asset generation counts
            for key, value in config.ASSET_GENERATION_COUNTS.items():
                self.config_vars[f"ASSET_{key}"] = tk.IntVar(value=value)
                
        except Exception as e:
            messagebox.showerror("Error", f"Failed to load config: {e}")
            
    def create_ui(self):
        # Main container with scrollbar
        main_frame = ttk.Frame(self.root, padding=10)
        main_frame.pack(fill=tk.BOTH, expand=True)
        
        # Canvas for scrolling
        canvas = tk.Canvas(main_frame, bg=self.bg_color, highlightthickness=0)
        scrollbar = ttk.Scrollbar(main_frame, orient="vertical", command=canvas.yview)
        self.scrollable_frame = ttk.Frame(canvas)
        
        self.scrollable_frame.bind(
            "<Configure>",
            lambda e: canvas.configure(scrollregion=canvas.bbox("all"))
        )
        
        canvas.create_window((0, 0), window=self.scrollable_frame, anchor="nw")
        canvas.configure(yscrollcommand=scrollbar.set)
        
        # Mouse wheel scrolling
        def on_mousewheel(event):
            canvas.yview_scroll(int(-1*(event.delta/120)), "units")
        canvas.bind_all("<MouseWheel>", on_mousewheel)
        
        canvas.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")
        
        # Title
        title_label = ttk.Label(
            self.scrollable_frame, 
            text="ThemeAssetGen Configuration", 
            style="Header.TLabel",
            font=("Segoe UI", 16, "bold")
        )
        title_label.pack(pady=(0, 20))
        
        # Service URLs Section
        self.create_section("Service URLs", [
            ("Ollama URL", "OLLAMA_URL", "string"),
            ("Ollama Model", "OLLAMA_MODEL", "string"),
            ("ComfyUI URL", "COMFYUI_URL", "string"),
            ("Hunyuan3D URL", "HUNYUAN3D_URL", "string"),
        ])
        
        # Timeouts Section
        self.create_section("Timeout Settings (seconds)", [
            ("Ollama Timeout", "OLLAMA_TIMEOUT", "int", 10, 600),
            ("ComfyUI Timeout", "COMFYUI_TIMEOUT", "int", 10, 1200),
            ("Hunyuan3D Timeout", "HUNYUAN3D_TIMEOUT", "int", 10, 1200),
        ])
        
        # Server Section
        self.create_section("Server Settings", [
            ("Server Host", "SERVER_HOST", "string"),
            ("Server Port", "SERVER_PORT", "int", 1, 65535),
        ])
        
        # Asset Generation Counts Section
        asset_items = [
            ("Wall Texture", "ASSET_wall_texture", "int", 1, 50),
            ("Stair", "ASSET_stair", "int", 1, 20),
            ("Floor Texture", "ASSET_floor_texture", "int", 1, 50),
            ("Door", "ASSET_door", "int", 1, 20),
            ("Small Prop", "ASSET_prop_small", "int", 1, 50),
            ("Medium Prop", "ASSET_prop_medium", "int", 1, 50),
            ("Large Prop", "ASSET_prop_large", "int", 1, 50),
        ]
        self.create_section("Asset Generation Counts (per theme)", asset_items)
        
        # Buttons
        button_frame = ttk.Frame(self.scrollable_frame)
        button_frame.pack(pady=20, fill="x")
        
        save_btn = tk.Button(
            button_frame, 
            text="Save Configuration", 
            command=self.save_config,
            bg=self.success_color,
            fg="#1e1e2e",
            font=("Segoe UI", 11, "bold"),
            padx=20,
            pady=8,
            cursor="hand2"
        )
        save_btn.pack(side="left", padx=5)
        
        reload_btn = tk.Button(
            button_frame, 
            text="Reload", 
            command=self.reload_config,
            bg=self.warning_color,
            fg="#1e1e2e",
            font=("Segoe UI", 11),
            padx=20,
            pady=8,
            cursor="hand2"
        )
        reload_btn.pack(side="left", padx=5)
        
        close_btn = tk.Button(
            button_frame, 
            text="Close", 
            command=self.root.quit,
            bg=self.button_bg,
            fg=self.fg_color,
            font=("Segoe UI", 11),
            padx=20,
            pady=8,
            cursor="hand2"
        )
        close_btn.pack(side="right", padx=5)
        
    def create_section(self, title, items):
        """Create a labeled section with input fields"""
        frame = ttk.LabelFrame(self.scrollable_frame, text=title, padding=15)
        frame.pack(fill="x", pady=10, padx=5)
        
        for item in items:
            row = ttk.Frame(frame)
            row.pack(fill="x", pady=5)
            
            label = ttk.Label(row, text=item[0], width=20)
            label.pack(side="left")
            
            var_name = item[1]
            var_type = item[2]
            
            if var_type == "string":
                entry = tk.Entry(
                    row, 
                    textvariable=self.config_vars[var_name],
                    bg=self.entry_bg,
                    fg=self.fg_color,
                    insertbackground=self.fg_color,
                    font=("Consolas", 10),
                    width=40
                )
                entry.pack(side="left", fill="x", expand=True)
            elif var_type == "int":
                min_val = item[3] if len(item) > 3 else 0
                max_val = item[4] if len(item) > 4 else 9999
                
                spinbox = tk.Spinbox(
                    row,
                    from_=min_val,
                    to=max_val,
                    textvariable=self.config_vars[var_name],
                    bg=self.entry_bg,
                    fg=self.fg_color,
                    insertbackground=self.fg_color,
                    font=("Consolas", 10),
                    width=15
                )
                spinbox.pack(side="left")
                
    def save_config(self):
        """Save configuration to config.py"""
        try:
            # Read original file
            with open(CONFIG_FILE, 'r', encoding='utf-8-sig') as f:
                content = f.read()
            
            # Build new ASSET_GENERATION_COUNTS
            asset_counts = {}
            for key in ["wall_texture", "stair", "floor_texture", "door", "prop_small", "prop_medium", "prop_large"]:
                asset_counts[key] = self.config_vars[f"ASSET_{key}"].get()
            
            # Generate new config content
            new_content = self.generate_config_content(asset_counts)
            
            # Write to file
            with open(CONFIG_FILE, 'w', encoding='utf-8-sig') as f:
                f.write(new_content)
                
            messagebox.showinfo("Success", "Configuration saved successfully!\n\nRestart the server to apply changes.")
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to save config: {e}")
            
    def generate_config_content(self, asset_counts):
        """Generate the complete config.py content"""
        content = '''import os
from pathlib import Path

# Project paths
BASE_DIR = Path(__file__).resolve().parent.parent
DATA_DIR = BASE_DIR / "data"
CATALOGS_DIR = DATA_DIR / "catalogs"
DATABASE_PATH = DATA_DIR / "database.db"

# Create directories
DATA_DIR.mkdir(exist_ok=True)
CATALOGS_DIR.mkdir(exist_ok=True)

# Service URLs
OLLAMA_URL = os.environ.get("OLLAMA_URL", "{ollama_url}")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "{ollama_model}")

COMFYUI_URL = os.environ.get("COMFYUI_URL", "{comfyui_url}")
COMFYUI_WORKFLOW_PATH = os.environ.get("COMFYUI_WORKFLOW_PATH", str(BASE_DIR / "backend" / "comfyuiapi" / "zit_assetgen_api.json"))

HUNYUAN3D_URL = os.environ.get("HUNYUAN3D_URL", "{hunyuan3d_url}")

# Timeout settings (seconds)
OLLAMA_TIMEOUT = int(os.environ.get("OLLAMA_TIMEOUT", "{ollama_timeout}"))
COMFYUI_TIMEOUT = int(os.environ.get("COMFYUI_TIMEOUT", "{comfyui_timeout}"))
HUNYUAN3D_TIMEOUT = int(os.environ.get("HUNYUAN3D_TIMEOUT", "{hunyuan3d_timeout}"))

# Server settings
SERVER_HOST = os.environ.get("SERVER_HOST", "{server_host}")
SERVER_PORT = int(os.environ.get("SERVER_PORT", "{server_port}"))

# ===================================================
# Asset Generation Count Settings (per type)
# ===================================================
# Modify these values to change how many assets
# are generated for each type when creating a theme
ASSET_GENERATION_COUNTS = {{
    "wall_texture": {wall_texture},    # Wall textures (tileable panels)
    "stair": {stair},            # Stairs (low, medium, high)
    "floor_texture": {floor_texture},   # Floor textures (tileable panels)
    "door": {door},             # Door styles
    "prop_small": {prop_small},      # Small props (books, cups, bottles, etc.)
    "prop_medium": {prop_medium},     # Medium props (chairs, baskets, boxes, etc.)
    "prop_large": {prop_large},      # Large props (tables, wardrobes, statues, etc.)
}}
'''
        return content.format(
            ollama_url=self.config_vars["OLLAMA_URL"].get(),
            ollama_model=self.config_vars["OLLAMA_MODEL"].get(),
            comfyui_url=self.config_vars["COMFYUI_URL"].get(),
            hunyuan3d_url=self.config_vars["HUNYUAN3D_URL"].get(),
            ollama_timeout=self.config_vars["OLLAMA_TIMEOUT"].get(),
            comfyui_timeout=self.config_vars["COMFYUI_TIMEOUT"].get(),
            hunyuan3d_timeout=self.config_vars["HUNYUAN3D_TIMEOUT"].get(),
            server_host=self.config_vars["SERVER_HOST"].get(),
            server_port=self.config_vars["SERVER_PORT"].get(),
            wall_texture=asset_counts["wall_texture"],
            stair=asset_counts["stair"],
            floor_texture=asset_counts["floor_texture"],
            door=asset_counts["door"],
            prop_small=asset_counts["prop_small"],
            prop_medium=asset_counts["prop_medium"],
            prop_large=asset_counts["prop_large"],
        )
        
    def reload_config(self):
        """Reload configuration from file"""
        # Clear module cache
        import importlib
        from backend import config
        importlib.reload(config)
        
        # Reload values
        self.config_vars["OLLAMA_URL"].set(config.OLLAMA_URL)
        self.config_vars["OLLAMA_MODEL"].set(config.OLLAMA_MODEL)
        self.config_vars["COMFYUI_URL"].set(config.COMFYUI_URL)
        self.config_vars["HUNYUAN3D_URL"].set(config.HUNYUAN3D_URL)
        self.config_vars["OLLAMA_TIMEOUT"].set(config.OLLAMA_TIMEOUT)
        self.config_vars["COMFYUI_TIMEOUT"].set(config.COMFYUI_TIMEOUT)
        self.config_vars["HUNYUAN3D_TIMEOUT"].set(config.HUNYUAN3D_TIMEOUT)
        self.config_vars["SERVER_HOST"].set(config.SERVER_HOST)
        self.config_vars["SERVER_PORT"].set(config.SERVER_PORT)
        
        for key, value in config.ASSET_GENERATION_COUNTS.items():
            self.config_vars[f"ASSET_{key}"].set(value)
            
        messagebox.showinfo("Success", "Configuration reloaded!")


def main():
    root = tk.Tk()
    app = ConfigEditor(root)
    root.mainloop()


if __name__ == "__main__":
    main()
