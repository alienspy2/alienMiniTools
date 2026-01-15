import io
import zipfile
from pathlib import Path
from urllib.parse import quote

from fastapi import APIRouter, Depends, HTTPException
from fastapi.responses import StreamingResponse
from sqlalchemy.orm import Session

from backend.models import (
    get_db, Catalog, Asset, Theme, AssetCategory,
    CatalogResponse, CatalogListResponse, CatalogListItem,
    GenerationStatus
)
from backend.api.routes.asset import asset_to_response
from backend.services.ollama_service import OllamaService

router = APIRouter()


@router.get("/list", response_model=CatalogListResponse)
async def list_catalogs(db: Session = Depends(get_db)):
    """카탈로그 목록 조회"""
    catalogs = db.query(Catalog).order_by(Catalog.created_at.desc()).all()

    items = []
    for catalog in catalogs:
        asset_count = len(catalog.assets)
        completed_count = len([a for a in catalog.assets if a.status == GenerationStatus.COMPLETED])
        items.append(CatalogListItem(
            id=catalog.id,
            name=catalog.name,
            theme_name=catalog.theme.name if catalog.theme else "",
            asset_count=asset_count,
            completed_count=completed_count,
            created_at=catalog.created_at,
        ))

    return CatalogListResponse(catalogs=items)


@router.get("/{catalog_id}", response_model=CatalogResponse)
async def get_catalog(catalog_id: str, db: Session = Depends(get_db)):
    """카탈로그 상세 조회"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    asset_count = len(catalog.assets)
    completed_count = len([a for a in catalog.assets if a.status == GenerationStatus.COMPLETED])

    return CatalogResponse(
        id=catalog.id,
        name=catalog.name,
        theme_id=catalog.theme_id,
        theme_name=catalog.theme.name if catalog.theme else "",
        description=catalog.description,
        asset_count=asset_count,
        completed_count=completed_count,
        assets=[asset_to_response(a) for a in catalog.assets],
        created_at=catalog.created_at,
        updated_at=catalog.updated_at,
    )


@router.delete("/{catalog_id}")
async def delete_catalog(catalog_id: str, db: Session = Depends(get_db)):
    """카탈로그 삭제"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    # Theme도 함께 삭제
    theme = catalog.theme
    db.delete(catalog)
    if theme:
        db.delete(theme)
    db.commit()
    return {"message": "Catalog deleted"}


@router.post("/{catalog_id}/generate-2d")
async def generate_2d_assets(catalog_id: str, db: Session = Depends(get_db)):
    """2D 이미지 배치 생성 시작"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    import asyncio
    import logging
    logger = logging.getLogger(__name__)

    async def run_2d_batch():
        from backend.models.database import SessionLocal
        from backend.services.pipeline_service import PipelineService

        batch_db = SessionLocal()
        try:
            logger.info(f"[BATCH-2D-API] 2D 배치 생성 시작: {catalog_id}")
            pipeline = PipelineService(batch_db)
            await pipeline.generate_2d_batch(catalog_id)
            logger.info(f"[BATCH-2D-API] 2D 배치 생성 완료: {catalog_id}")
        except Exception as e:
            logger.error(f"[BATCH-2D-API] 2D 배치 생성 실패: {catalog_id} - {e}")
        finally:
            batch_db.close()

    asyncio.create_task(run_2d_batch())
    return {"message": "2D batch generation started", "catalog_id": catalog_id}


@router.post("/{catalog_id}/generate-3d")
async def generate_3d_assets(catalog_id: str, db: Session = Depends(get_db)):
    """3D 모델 배치 생성 시작 (2D 이미지가 있는 에셋만)"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    import asyncio
    import logging
    logger = logging.getLogger(__name__)

    async def run_3d_batch():
        from backend.models.database import SessionLocal
        from backend.services.pipeline_service import PipelineService

        batch_db = SessionLocal()
        try:
            logger.info(f"[BATCH-3D-API] 3D 배치 생성 시작: {catalog_id}")
            pipeline = PipelineService(batch_db)
            await pipeline.generate_3d_batch(catalog_id)
            logger.info(f"[BATCH-3D-API] 3D 배치 생성 완료: {catalog_id}")
        except Exception as e:
            logger.error(f"[BATCH-3D-API] 3D 배치 생성 실패: {catalog_id} - {e}")
        finally:
            batch_db.close()

    asyncio.create_task(run_3d_batch())
    return {"message": "3D batch generation started", "catalog_id": catalog_id}


@router.get("/{catalog_id}/export")
async def export_catalog(catalog_id: str, db: Session = Depends(get_db)):
    """카탈로그를 ZIP으로 내보내기"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    # ZIP 파일 생성
    zip_buffer = io.BytesIO()
    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED) as zf:
        for asset in catalog.assets:
            if asset.status != GenerationStatus.COMPLETED:
                continue

            # 2D 프리뷰 이미지
            if asset.preview_image_path:
                preview_path = Path(asset.preview_image_path)
                if preview_path.exists():
                    zf.write(preview_path, f"{asset.name}/preview.png")

            # 3D GLB 모델
            if asset.model_glb_path:
                glb_path = Path(asset.model_glb_path)
                if glb_path.exists():
                    zf.write(glb_path, f"{asset.name}/model.glb")

            # 3D OBJ 모델
            if asset.model_obj_path:
                obj_path = Path(asset.model_obj_path)
                if obj_path.exists():
                    zf.write(obj_path, f"{asset.name}/model.obj")

    zip_buffer.seek(0)

    # 한글 파일명을 위한 RFC 5987 인코딩 (safe='' 로 모든 특수문자 인코딩)
    filename_encoded = quote(f"{catalog.name}.zip", safe='')

    return StreamingResponse(
        zip_buffer,
        media_type="application/zip",
        headers={
            "Content-Disposition": f"attachment; filename*=UTF-8''{filename_encoded}"
        }
    )


@router.post("/{catalog_id}/add-assets")
async def add_assets_to_catalog(catalog_id: str, count: int = 10, db: Session = Depends(get_db)):
    """카탈로그에 에셋 추가 (Ollama로 테마에 맞는 에셋 생성)"""
    import logging
    logger = logging.getLogger(__name__)

    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    # 테마 이름 가져오기
    theme_name = catalog.theme.name if catalog.theme else catalog.name

    # 기존 에셋 이름 목록
    existing_names = [asset.name for asset in catalog.assets]

    logger.info(f"[ADD-ASSETS] 카탈로그 {catalog_id}에 {count}개 에셋 추가 요청")
    logger.info(f"[ADD-ASSETS] 테마: {theme_name}, 기존 에셋: {len(existing_names)}개")

    # Ollama로 추가 에셋 생성
    ollama = OllamaService()
    try:
        new_assets_data = await ollama.generate_additional_assets(
            theme=theme_name,
            existing_names=existing_names,
            count=count
        )
    except Exception as e:
        logger.error(f"[ADD-ASSETS] Ollama 에셋 생성 실패: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to generate assets: {e}")

    # DB에 새 에셋 추가
    added_assets = []
    for asset_data in new_assets_data:
        try:
            category_str = asset_data.get("category", "other").lower()
            category = AssetCategory(category_str) if category_str in [c.value for c in AssetCategory] else AssetCategory.OTHER

            new_asset = Asset(
                catalog_id=catalog_id,
                name=asset_data.get("name", "unnamed_asset"),
                name_kr=asset_data.get("name_kr", ""),
                category=category,
                description=asset_data.get("description", ""),
                prompt_2d=asset_data.get("prompt_2d", ""),
                status=GenerationStatus.PENDING,
            )
            db.add(new_asset)
            added_assets.append(new_asset)
        except Exception as e:
            logger.warning(f"[ADD-ASSETS] 에셋 추가 실패: {asset_data.get('name', 'unknown')} - {e}")

    db.commit()

    logger.info(f"[ADD-ASSETS] {len(added_assets)}개 에셋 추가 완료")

    return {
        "message": f"Added {len(added_assets)} assets",
        "catalog_id": catalog_id,
        "added_count": len(added_assets),
    }
