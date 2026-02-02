import os
from pathlib import Path

# 프로젝트 경로
BASE_DIR = Path(__file__).resolve().parent.parent
DATA_DIR = BASE_DIR / "data"
CATALOGS_DIR = DATA_DIR / "catalogs"
DATABASE_PATH = DATA_DIR / "database.db"

# 디렉토리 생성
DATA_DIR.mkdir(exist_ok=True)
CATALOGS_DIR.mkdir(exist_ok=True)

# 서비스 URL
OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://127.0.0.1:11434")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "gemma3:4b")

COMFYUI_URL = os.environ.get("COMFYUI_URL", "http://127.0.0.1:23000")
COMFYUI_WORKFLOW_PATH = os.environ.get("COMFYUI_WORKFLOW_PATH", str(BASE_DIR / "backend" / "workflows" / "asset_generation.json"))

HUNYUAN3D_URL = os.environ.get("HUNYUAN3D_URL", "http://127.0.0.1:23003")

# 타임아웃 설정 (초)
OLLAMA_TIMEOUT = int(os.environ.get("OLLAMA_TIMEOUT", "120"))
COMFYUI_TIMEOUT = int(os.environ.get("COMFYUI_TIMEOUT", "600"))
HUNYUAN3D_TIMEOUT = int(os.environ.get("HUNYUAN3D_TIMEOUT", "600"))

# 서버 설정
SERVER_HOST = os.environ.get("SERVER_HOST", "0.0.0.0")
SERVER_PORT = int(os.environ.get("SERVER_PORT", "8000"))
