import os
import sys
import subprocess
from fastapi import APIRouter

router = APIRouter()

@router.post("/open-config")
async def open_config_editor():
    """Open the configuration editor GUI"""
    try:
        # Assuming edit_config.py is in the project root
        project_root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
        # Adjust path logic if needed. 
        # API file: backend/api/routes/system.py
        # Root: backend/../
        
        # Simpler: use cwd if we trust run server sets it.
        # But let's be safe.
        
        # We will just run "python edit_config.py" in a detached process
        subprocess.Popen(["python", "edit_config.py"], cwd=os.getcwd(), shell=True)
        return {"message": "Config editor opened"}
    except Exception as e:
        return {"error": str(e)}

@router.post("/restart")
async def restart_server():
    """Restart the server"""
    import logging
    import signal
    logger = logging.getLogger(__name__)
    
    logger.info("Restart requested via API")
    print("!!! SERVER RESTART REQUESTED !!!")
    
    # Scheduling exit
    import threading
    import time
    
    def kill_server():
        logger.info("Stopping server process in 1 second...")
        print("Stopping server process...")
        time.sleep(1)
        print("Killing process tree...")
        
        # When uvicorn runs with reload=True, there's a parent (reloader) and child (app) process.
        # os._exit only kills the child. We need to kill the parent too.
        parent_pid = os.getppid()
        current_pid = os.getpid()
        
        print(f"Current PID: {current_pid}, Parent PID: {parent_pid}")
        
        try:
            # Kill parent first (reloader), then self
            os.kill(parent_pid, signal.SIGTERM)
        except Exception as e:
            print(f"Failed to kill parent: {e}")
        
        # Exit this process too
        os._exit(0)
        
    threading.Thread(target=kill_server).start()
    
    return {"message": "Server restarting..."}

@router.get("/server-status")
async def check_server_status():
    """Check status of helper services"""
    from backend.services.ollama_service import OllamaService
    from backend.services.comfyui_service import ComfyUIService
    from backend.services.hunyuan2_service import Hunyuan3DService
    
    ollama = OllamaService()
    comfy = ComfyUIService()
    hunyuan = Hunyuan3DService()
    
    # Run checks in parallel? They are async methods.
    # Note: check_health methods are async.
    status = {
        "ollama": await ollama.check_health(),
        "comfyui": await comfy.check_health(),
        "hunyuan": await hunyuan.check_health()
    }
    return status
