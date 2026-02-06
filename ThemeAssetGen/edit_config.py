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

from backend.config_manager import config_manager


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
        """Load current configuration from config_manager"""
        try:
            config_manager.load_config()
            
            # Service URLs
            self.config_vars["OLLAMA_URL"] = tk.StringVar(value=config_manager.get("OLLAMA_URL"))
            self.config_vars["OLLAMA_MODEL"] = tk.StringVar(value=config_manager.get("OLLAMA_MODEL"))
            self.config_vars["COMFYUI_URL"] = tk.StringVar(value=config_manager.get("COMFYUI_URL"))
            self.config_vars["HUNYUAN3D_URL"] = tk.StringVar(value=config_manager.get("HUNYUAN3D_URL"))
            
            # Model Settings
            self.config_vars["COMFYUI_UNET_MODEL"] = tk.StringVar(value=config_manager.get("COMFYUI_UNET_MODEL"))
            
            # Timeouts
            self.config_vars["OLLAMA_TIMEOUT"] = tk.IntVar(value=config_manager.get("OLLAMA_TIMEOUT"))
            self.config_vars["COMFYUI_TIMEOUT"] = tk.IntVar(value=config_manager.get("COMFYUI_TIMEOUT"))
            self.config_vars["HUNYUAN3D_TIMEOUT"] = tk.IntVar(value=config_manager.get("HUNYUAN3D_TIMEOUT"))
            
            # Server
            self.config_vars["SERVER_HOST"] = tk.StringVar(value=config_manager.get("SERVER_HOST"))
            self.config_vars["SERVER_PORT"] = tk.IntVar(value=config_manager.get("SERVER_PORT"))
            
            # Asset generation counts
            asset_counts = config_manager.get("ASSET_GENERATION_COUNTS")
            for key, value in asset_counts.items():
                if f"ASSET_{key}" not in self.config_vars:
                    # Dynamically create var if it doesn't exist yet (for new categories)
                    self.config_vars[f"ASSET_{key}"] = tk.IntVar(value=value)
                else:
                    self.config_vars[f"ASSET_{key}"].set(value)
                
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

        # Model Settings Section
        self.create_section("Model Settings", [
            ("ComfyUI UNET Model", "COMFYUI_UNET_MODEL", "dropdown", [
                "z_image_turbo_nvfp4.safetensors",
                "z_image_turbo_bf16.safetensors"
            ]),
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
            elif var_type == "dropdown":
                values = item[3] if len(item) > 3 else []
                combobox = ttk.Combobox(
                    row,
                    textvariable=self.config_vars[var_name],
                    values=values,
                    state="readonly",
                    font=("Consolas", 10),
                    width=38
                )
                combobox.pack(side="left", fill="x", expand=True)
                
    def save_config(self):
        """Save configuration to config.json via ConfigManager"""
        try:
            # Update values in config_manager
            config_manager.set("OLLAMA_URL", self.config_vars["OLLAMA_URL"].get())
            config_manager.set("OLLAMA_MODEL", self.config_vars["OLLAMA_MODEL"].get())
            config_manager.set("COMFYUI_URL", self.config_vars["COMFYUI_URL"].get())
            config_manager.set("HUNYUAN3D_URL", self.config_vars["HUNYUAN3D_URL"].get())
            config_manager.set("COMFYUI_UNET_MODEL", self.config_vars["COMFYUI_UNET_MODEL"].get())
            
            config_manager.set("OLLAMA_TIMEOUT", self.config_vars["OLLAMA_TIMEOUT"].get())
            config_manager.set("COMFYUI_TIMEOUT", self.config_vars["COMFYUI_TIMEOUT"].get())
            config_manager.set("HUNYUAN3D_TIMEOUT", self.config_vars["HUNYUAN3D_TIMEOUT"].get())
            
            config_manager.set("SERVER_HOST", self.config_vars["SERVER_HOST"].get())
            config_manager.set("SERVER_PORT", self.config_vars["SERVER_PORT"].get())
            
            # Asset counts
            asset_counts = config_manager.get("ASSET_GENERATION_COUNTS").copy()
            # Only update keys that we have vars for (in this UI)
            # Todo: make this UI dynamic based on config, but for now fixed
            for key in ["wall_texture", "stair", "floor_texture", "door", "prop_small", "prop_medium", "prop_large"]:
                if f"ASSET_{key}" in self.config_vars:
                    asset_counts[key] = self.config_vars[f"ASSET_{key}"].get()
            
            config_manager.set("ASSET_GENERATION_COUNTS", asset_counts)
                
            messagebox.showinfo("Success", "Configuration saved successfully!")
            
        except Exception as e:
            messagebox.showerror("Error", f"Failed to save config: {e}")

    def reload_config(self):
        """Reload configuration from file"""
        self.load_config()
        messagebox.showinfo("Success", "Configuration reloaded!")


def main():
    root = tk.Tk()
    app = ConfigEditor(root)
    root.mainloop()


if __name__ == "__main__":
    main()
