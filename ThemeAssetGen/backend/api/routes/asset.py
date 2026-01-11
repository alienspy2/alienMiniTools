from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.orm import Session

from backend.models import get_db, Asset, AssetResponse, AssetEditRequest, GenerationStatus

router = APIRouter()


def asset_to_response(asset: Asset) -> AssetResponse:
    """Asset 엔티티를 응답 스키마로 변환"""
    # 캐시 버스팅을 위한 타임스탬프
    cache_buster = int(asset.updated_at.timestamp()) if asset.updated_at else 0

    return AssetResponse(
        id=asset.id,
        name=asset.name,
        name_kr=asset.name_kr,
        category=asset.category.value if asset.category else "other",
        description=asset.description,
        prompt_2d=asset.prompt_2d,
        status=asset.status.value if asset.status else "pending",
        error_message=asset.error_message,
        preview_url=f"/files/preview/{asset.id}?t={cache_buster}" if asset.preview_image_path else None,
        model_glb_url=f"/files/model/{asset.id}/glb" if asset.model_glb_path else None,
        model_obj_url=f"/files/model/{asset.id}/obj" if asset.model_obj_path else None,
        created_at=asset.created_at,
        updated_at=asset.updated_at,
    )


@router.get("/{asset_id}", response_model=AssetResponse)
async def get_asset(asset_id: str, db: Session = Depends(get_db)):
    """에셋 상세 조회"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")
    return asset_to_response(asset)


@router.put("/{asset_id}", response_model=AssetResponse)
async def update_asset(
    asset_id: str,
    request: AssetEditRequest,
    db: Session = Depends(get_db)
):
    """에셋 정보 수정"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    from backend.models.entities import AssetCategory

    if request.name is not None:
        asset.name = request.name
    if request.name_kr is not None:
        asset.name_kr = request.name_kr
    if request.category is not None:
        try:
            asset.category = AssetCategory(request.category)
        except ValueError:
            asset.category = AssetCategory.OTHER
    if request.description is not None:
        asset.description = request.description
    if request.prompt_2d is not None:
        asset.prompt_2d = request.prompt_2d

    db.commit()
    db.refresh(asset)
    return asset_to_response(asset)


@router.delete("/{asset_id}")
async def delete_asset(asset_id: str, db: Session = Depends(get_db)):
    """에셋 삭제"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    db.delete(asset)
    db.commit()
    return {"message": "Asset deleted"}


@router.post("/{asset_id}/generate", response_model=AssetResponse)
async def generate_asset(asset_id: str, db: Session = Depends(get_db)):
    """단일 에셋 2D→3D 생성"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    from backend.services.pipeline_service import PipelineService

    pipeline = PipelineService(db)
    updated_asset = await pipeline.generate_single_asset(asset_id)
    return asset_to_response(updated_asset)


@router.post("/{asset_id}/generate-2d", response_model=AssetResponse)
async def generate_asset_2d(asset_id: str, db: Session = Depends(get_db)):
    """단일 에셋 2D 이미지만 생성 (재생성 가능)"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    from backend.services.pipeline_service import PipelineService

    pipeline = PipelineService(db)
    updated_asset = await pipeline._generate_2d_only(asset_id)
    return asset_to_response(updated_asset)


@router.post("/{asset_id}/generate-3d", response_model=AssetResponse)
async def generate_asset_3d(asset_id: str, db: Session = Depends(get_db)):
    """단일 에셋 3D 모델만 생성 (2D 이미지 필요)"""
    asset = db.query(Asset).filter(Asset.id == asset_id).first()
    if not asset:
        raise HTTPException(status_code=404, detail="Asset not found")

    if not asset.preview_image_path:
        raise HTTPException(status_code=400, detail="2D 이미지가 없습니다. 먼저 2D를 생성하세요.")

    from backend.services.pipeline_service import PipelineService

    pipeline = PipelineService(db)
    updated_asset = await pipeline._generate_3d_only(asset_id)
    return asset_to_response(updated_asset)
