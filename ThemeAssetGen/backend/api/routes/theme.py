import asyncio
import json
from fastapi import APIRouter, Depends, HTTPException
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session

from backend.models import get_db, ThemeGenerateRequest, ThemeGenerateResponse

router = APIRouter()


# In-memory progress storage
theme_progress = {}


def set_progress(session_id: str, data: dict):
    theme_progress[session_id] = data


def get_progress(session_id: str):
    return theme_progress.get(session_id)


def clear_progress(session_id: str):
    if session_id in theme_progress:
        del theme_progress[session_id]


@router.post("/generate-stream")
async def generate_theme_assets_stream(
    request: ThemeGenerateRequest,
    db: Session = Depends(get_db)
):
    """Generate theme assets with SSE progress stream"""
    import uuid
    from backend.services.pipeline_service import PipelineService

    session_id = str(uuid.uuid4())
    
    async def event_generator():
        pipeline = PipelineService(db)
        result_holder = {"result": None, "error": None}
        
        def progress_callback(data):
            set_progress(session_id, data)
        
        async def run_generation():
            try:
                result = await pipeline.generate_theme_assets(
                    request.theme, 
                    progress_callback=progress_callback
                )
                result_holder["result"] = result
            except Exception as e:
                result_holder["error"] = str(e)
        
        # Start generation task
        task = asyncio.create_task(run_generation())
        
        # Stream progress updates
        last_sent = None
        while not task.done():
            progress = get_progress(session_id)
            if progress and progress != last_sent:
                yield f"data: {json.dumps(progress)}\n\n"
                last_sent = progress.copy() if isinstance(progress, dict) else progress
            await asyncio.sleep(0.5)
        
        # Wait for task to complete
        await task
        
        # Send final result
        if result_holder["error"]:
            yield f"data: {json.dumps({'stage': 'error', 'message': result_holder['error']})}\n\n"
        else:
            result = result_holder["result"]
            yield f"data: {json.dumps({'stage': 'done', 'catalog_id': result.catalog_id, 'theme_id': result.theme_id, 'asset_count': len(result.assets)})}\n\n"
        
        clear_progress(session_id)
    
    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        }
    )


@router.post("/generate", response_model=ThemeGenerateResponse)
async def generate_theme_assets(
    request: ThemeGenerateRequest,
    db: Session = Depends(get_db)
):
    """Generate theme assets (non-streaming fallback)"""
    from backend.services.pipeline_service import PipelineService

    pipeline = PipelineService(db)
    result = await pipeline.generate_theme_assets(request.theme)
    return result
