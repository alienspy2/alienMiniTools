import os
from pathlib import Path
from backend.config_manager import config_manager

# Project paths (Constants)
BASE_DIR = Path(__file__).resolve().parent.parent
DATA_DIR = BASE_DIR / "data"
CATALOGS_DIR = DATA_DIR / "catalogs"
DATABASE_PATH = DATA_DIR / "database.db"

# Create directories
DATA_DIR.mkdir(exist_ok=True)
CATALOGS_DIR.mkdir(exist_ok=True)

# Service URLs
OLLAMA_URL = os.environ.get("OLLAMA_URL", config_manager.get("OLLAMA_URL"))
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", config_manager.get("OLLAMA_MODEL"))

COMFYUI_URL = os.environ.get("COMFYUI_URL", config_manager.get("COMFYUI_URL"))
COMFYUI_WORKFLOW_PATH = os.environ.get("COMFYUI_WORKFLOW_PATH", config_manager.get("COMFYUI_WORKFLOW_PATH"))
COMFYUI_UNET_MODEL = os.environ.get("COMFYUI_UNET_MODEL", config_manager.get("COMFYUI_UNET_MODEL"))

HUNYUAN3D_URL = os.environ.get("HUNYUAN3D_URL", config_manager.get("HUNYUAN3D_URL"))

# Timeout settings (seconds)
OLLAMA_TIMEOUT = int(os.environ.get("OLLAMA_TIMEOUT", config_manager.get("OLLAMA_TIMEOUT")))
COMFYUI_TIMEOUT = int(os.environ.get("COMFYUI_TIMEOUT", config_manager.get("COMFYUI_TIMEOUT")))
HUNYUAN3D_TIMEOUT = int(os.environ.get("HUNYUAN3D_TIMEOUT", config_manager.get("HUNYUAN3D_TIMEOUT")))

# Server settings
SERVER_HOST = os.environ.get("SERVER_HOST", config_manager.get("SERVER_HOST"))
SERVER_PORT = int(os.environ.get("SERVER_PORT", config_manager.get("SERVER_PORT")))

# ===================================================
# Asset Generation Count Settings (per type)
# ===================================================
ASSET_GENERATION_COUNTS = config_manager.get("ASSET_GENERATION_COUNTS")
