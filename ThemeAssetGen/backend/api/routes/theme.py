from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session

from backend.models import get_db, ThemeGenerateRequest, ThemeGenerateResponse

router = APIRouter()


@router.post("/generate", response_model=ThemeGenerateResponse)
async def generate_theme_assets(
    request: ThemeGenerateRequest,
    db: Session = Depends(get_db)
):
    """테마를 입력받아 에셋 리스트 생성"""
    from backend.services.ollama_service import OllamaService
    from backend.services.pipeline_service import PipelineService

    ollama = OllamaService()
    pipeline = PipelineService(db)

    result = await pipeline.generate_theme_assets(request.theme)
    return result
