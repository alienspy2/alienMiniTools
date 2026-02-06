from pathlib import Path

# Default settings that will be used to generate config.json
DEFAULT_CONFIG = {
    "OLLAMA_URL": "http://127.0.0.1:11434",
    "OLLAMA_MODEL": "gemma3:4b",
    
    "COMFYUI_URL": "http://127.0.0.1:23000",
    # COMFYUI_WORKFLOW_PATH depends on runtime path, handled in manager/config.py if None
    # But for JSON, we can store a default string or handle it dynamically. 
    # Let's verify how it's used. It's better to store null/None and let the code resolve it, 
    # or store the resolved path.
    # For now, we'll exclude path-dependent variables from the static default dict if possible, 
    # or use a placeholder.
    "COMFYUI_WORKFLOW_PATH": None, 
    "COMFYUI_UNET_MODEL": "z_image_turbo_bf16.safetensors",
    
    "HUNYUAN3D_URL": "http://192.168.0.2:23003",
    
    "OLLAMA_TIMEOUT": 300,
    "COMFYUI_TIMEOUT": 600,
    "HUNYUAN3D_TIMEOUT": 600,
    
    "SERVER_HOST": "0.0.0.0",
    "SERVER_PORT": 8000,
    
    "ASSET_GENERATION_COUNTS": {
        "wall_texture": 10,
        "stair": 3,
        "floor_texture": 10,
        "door": 5,
        "prop_small": 20,
        "prop_medium": 20,
        "prop_large": 20,
    }
}
