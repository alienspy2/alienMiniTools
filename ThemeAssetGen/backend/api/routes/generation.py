import asyncio
import json

from fastapi import APIRouter, Depends
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session

from backend.models import get_db, Catalog, GenerationStatus, BatchGenerationStatus

router = APIRouter()

# 진행 상태 저장 (메모리)
generation_status: dict[str, BatchGenerationStatus] = {}


def get_status(catalog_id: str) -> BatchGenerationStatus:
    """카탈로그 생성 상태 조회"""
    return generation_status.get(catalog_id)


def set_status(catalog_id: str, status: BatchGenerationStatus):
    """카탈로그 생성 상태 설정"""
    generation_status[catalog_id] = status


@router.get("/stream/{catalog_id}")
async def stream_generation_status(catalog_id: str, db: Session = Depends(get_db)):
    """SSE로 배치 생성 진행률 스트리밍"""

    async def event_generator():
        while True:
            status = get_status(catalog_id)
            if status:
                data = json.dumps({
                    "catalog_id": status.catalog_id,
                    "total": status.total,
                    "completed": status.completed,
                    "failed": status.failed,
                    "current_asset": status.current_asset,
                    "current_status": status.current_status,
                })
                yield f"data: {data}\n\n"

                # 완료 확인
                if status.completed + status.failed >= status.total:
                    yield f"data: {json.dumps({'done': True})}\n\n"
                    break
            else:
                yield f"data: {json.dumps({'waiting': True})}\n\n"

            await asyncio.sleep(1)

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        }
    )


@router.get("/status/{catalog_id}")
async def get_generation_status(catalog_id: str):
    """배치 생성 상태 조회"""
    status = get_status(catalog_id)
    if not status:
        return {"status": "not_started"}
    return status
