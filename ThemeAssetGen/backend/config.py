import os
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
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "gemma3:4b")

COMFYUI_URL = os.environ.get("COMFYUI_URL", "http://127.0.0.1:23000")
COMFYUI_WORKFLOW_PATH = os.environ.get("COMFYUI_WORKFLOW_PATH", str(BASE_DIR / "backend" / "comfyuiapi" / "zit_assetgen_api.json"))

HUNYUAN3D_URL = os.environ.get("HUNYUAN3D_URL", "http://192.168.0.2:23003")

# Timeout settings (seconds)
OLLAMA_TIMEOUT = int(os.environ.get("OLLAMA_TIMEOUT", "300"))
COMFYUI_TIMEOUT = int(os.environ.get("COMFYUI_TIMEOUT", "600"))
HUNYUAN3D_TIMEOUT = int(os.environ.get("HUNYUAN3D_TIMEOUT", "600"))

# Server settings
SERVER_HOST = os.environ.get("SERVER_HOST", "0.0.0.0")
SERVER_PORT = int(os.environ.get("SERVER_PORT", "8000"))

# ===================================================
# Asset Generation Count Settings (per type)
# ===================================================
# Modify these values to change how many assets
# are generated for each type when creating a theme
ASSET_GENERATION_COUNTS = {
    "wall_texture": 10,    # Wall textures (tileable panels)
    "stair": 3,            # Stairs (low, medium, high)
    "floor_texture": 10,   # Floor textures (tileable panels)
    "door": 5,             # Door styles
    "prop_small": 10,      # Small props (books, cups, bottles, etc.)
    "prop_medium": 10,     # Medium props (chairs, baskets, boxes, etc.)
    "prop_large": 10,      # Large props (tables, wardrobes, statues, etc.)
}
