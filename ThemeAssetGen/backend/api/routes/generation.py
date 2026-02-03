import asyncio
import json
import logging
from datetime import datetime
from typing import Dict

from fastapi import APIRouter, Depends
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session

from backend.models import get_db, Catalog, GenerationStatus, BatchGenerationStatus
from backend.models.schemas import QueueItem, QueueStatus

router = APIRouter()
logger = logging.getLogger(__name__)

# === Simplified Queue Storage ===
# Key: catalog_id, Value: QueueStatus
queue_storage: Dict[str, QueueStatus] = {}


def get_queue(catalog_id: str) -> QueueStatus:
    """Get or create queue status for catalog"""
    if catalog_id not in queue_storage:
        queue_storage[catalog_id] = QueueStatus(catalog_id=catalog_id)
    return queue_storage[catalog_id]


def update_queue(catalog_id: str, status: QueueStatus):
    """Update queue status"""
    queue_storage[catalog_id] = status


def clear_queue(catalog_id: str):
    """Clear queue for catalog"""
    if catalog_id in queue_storage:
        del queue_storage[catalog_id]


# === Legacy compatibility ===
generation_status: dict[str, BatchGenerationStatus] = {}


def get_status(catalog_id: str) -> BatchGenerationStatus:
    """Get catalog generation status (legacy)"""
    return generation_status.get(catalog_id)


def set_status(catalog_id: str, status: BatchGenerationStatus):
    """Set catalog generation status (legacy)"""
    generation_status[catalog_id] = status


# === SSE Endpoint (Simplified) ===
@router.get("/stream/{catalog_id}")
async def stream_generation_status(catalog_id: str, db: Session = Depends(get_db)):
    """SSE for streaming batch generation progress"""

    async def event_generator():
        empty_count = 0
        while True:
            queue = get_queue(catalog_id)
            
            # Calculate totals
            total_2d = len(queue.queue_2d)
            total_3d = len(queue.queue_3d)
            
            completed_2d = len([q for q in queue.queue_2d if q.status == "completed"])
            completed_3d = len([q for q in queue.queue_3d if q.status == "completed"])
            
            failed_2d = len([q for q in queue.queue_2d if q.status == "failed"])
            failed_3d = len([q for q in queue.queue_3d if q.status == "failed"])
            
            pending_2d = [q for q in queue.queue_2d if q.status == "pending"]
            pending_3d = [q for q in queue.queue_3d if q.status == "pending"]
            
            # Build response data
            data = {
                "catalog_id": catalog_id,
                "is_running_2d": queue.is_running_2d,
                "is_running_3d": queue.is_running_3d,
                "total_2d": total_2d,
                "total_3d": total_3d,
                "completed_2d": completed_2d,
                "completed_3d": completed_3d,
                "failed_2d": failed_2d,
                "failed_3d": failed_3d,
                "current_2d": queue.current_2d.model_dump() if queue.current_2d else None,
                "current_3d": queue.current_3d.model_dump() if queue.current_3d else None,
                "pending_2d": [q.asset_name for q in pending_2d[:5]],
                "pending_3d": [q.asset_name for q in pending_3d[:5]],
                "pending_count_2d": len(pending_2d),
                "pending_count_3d": len(pending_3d),
            }
            
            yield f"data: {json.dumps(data)}\n\n"
            
            # Check if all done
            all_done_2d = not queue.is_running_2d and total_2d > 0 and (completed_2d + failed_2d) >= total_2d
            all_done_3d = not queue.is_running_3d and total_3d > 0 and (completed_3d + failed_3d) >= total_3d
            
            # If nothing is running and we have results, send done
            if not queue.is_running_2d and not queue.is_running_3d:
                if (total_2d > 0 or total_3d > 0) and (all_done_2d or total_2d == 0) and (all_done_3d or total_3d == 0):
                    yield f"data: {json.dumps({'done': True})}\n\n"
                    break
                    
                # If queue is completely empty
                if total_2d == 0 and total_3d == 0:
                    empty_count += 1
                    if empty_count > 5:  # Wait a bit for queue to be filled
                        yield f"data: {json.dumps({'waiting': True})}\n\n"
            else:
                empty_count = 0

            await asyncio.sleep(0.5)

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        }
    )


@router.get("/status/{catalog_id}")
async def get_generation_status_endpoint(catalog_id: str):
    """Get current queue status"""
    queue = get_queue(catalog_id)
    return queue.model_dump()


@router.post("/clear/{catalog_id}")
async def clear_generation_queue(catalog_id: str):
    """Clear queue for catalog"""
    clear_queue(catalog_id)
    return {"message": "Queue cleared"}
