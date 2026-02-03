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
    """List all catalogs"""
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
    """Get catalog details"""
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
    """Delete catalog"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    theme = catalog.theme
    db.delete(catalog)
    if theme:
        db.delete(theme)
    db.commit()
    return {"message": "Catalog deleted"}


@router.post("/{catalog_id}/generate-2d")
async def generate_2d_assets(catalog_id: str, db: Session = Depends(get_db)):
    """Start 2D image batch generation"""
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
            logger.info(f"[BATCH-2D-API] Starting 2D batch: {catalog_id}")
            pipeline = PipelineService(batch_db)
            await pipeline.generate_2d_batch(catalog_id)
            logger.info(f"[BATCH-2D-API] Completed 2D batch: {catalog_id}")
        except Exception as e:
            logger.error(f"[BATCH-2D-API] Failed 2D batch: {catalog_id} - {e}")
        finally:
            batch_db.close()

    asyncio.create_task(run_2d_batch())
    return {"message": "2D batch generation started", "catalog_id": catalog_id}


@router.post("/{catalog_id}/generate-3d")
async def generate_3d_assets(catalog_id: str, db: Session = Depends(get_db)):
    """Start 3D model batch generation (only assets with 2D images)"""
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
            logger.info(f"[BATCH-3D-API] Starting 3D batch: {catalog_id}")
            pipeline = PipelineService(batch_db)
            await pipeline.generate_3d_batch(catalog_id)
            logger.info(f"[BATCH-3D-API] Completed 3D batch: {catalog_id}")
        except Exception as e:
            logger.error(f"[BATCH-3D-API] Failed 3D batch: {catalog_id} - {e}")
        finally:
            batch_db.close()

    asyncio.create_task(run_3d_batch())
    return {"message": "3D batch generation started", "catalog_id": catalog_id}


@router.post("/{catalog_id}/generate-all")
async def generate_all_assets(catalog_id: str, db: Session = Depends(get_db)):
    """Start parallel 2D+3D pipeline: 2D completes -> immediately starts 3D"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    import asyncio
    import logging
    logger = logging.getLogger(__name__)

    async def run_parallel_pipeline():
        from backend.models.database import SessionLocal
        from backend.services.pipeline_service import PipelineService

        batch_db = SessionLocal()
        try:
            logger.info(f"[PARALLEL] Starting parallel pipeline: {catalog_id}")
            pipeline = PipelineService(batch_db)
            await pipeline.generate_all_parallel(catalog_id)
            logger.info(f"[PARALLEL] Completed parallel pipeline: {catalog_id}")
        except Exception as e:
            logger.error(f"[PARALLEL] Failed parallel pipeline: {catalog_id} - {e}")
            import traceback
            logger.error(traceback.format_exc())
        finally:
            batch_db.close()

    asyncio.create_task(run_parallel_pipeline())
    
    return {
        "message": "Parallel 2D+3D pipeline started (2D complete -> 3D starts immediately)",
        "catalog_id": catalog_id
    }




@router.get("/{catalog_id}/export")
async def export_catalog(catalog_id: str, db: Session = Depends(get_db)):
    """Export catalog as ZIP"""
    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    zip_buffer = io.BytesIO()
    with zipfile.ZipFile(zip_buffer, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as zf:
        for asset in catalog.assets:
            if asset.status != GenerationStatus.COMPLETED:
                continue

            # 2D preview image
            if asset.preview_image_path:
                preview_path = Path(asset.preview_image_path)
                if preview_path.exists():
                    zf.write(preview_path, f"{asset.name}/preview.png")

            # 3D GLB model
            if asset.model_glb_path:
                glb_path = Path(asset.model_glb_path)
                if glb_path.exists():
                    zf.write(glb_path, f"{asset.name}/model.glb")

            # 3D OBJ model
            if asset.model_obj_path:
                obj_path = Path(asset.model_obj_path)
                if obj_path.exists():
                    zf.write(obj_path, f"{asset.name}/model.obj")

            # description.txt
            description_content = f"""Name: {asset.name}
Name (Korean): {asset.name_kr or ''}
Category: {asset.category.value if asset.category else 'other'}

=== Description ===
{asset.description or 'No description available.'}

=== Generation Prompt ===
{asset.prompt_2d or 'No prompt available.'}
"""
            zf.writestr(f"{asset.name}/description.txt", description_content.encode('utf-8'))

    zip_buffer.seek(0)

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
    """Add assets to catalog (generate with Ollama)"""
    import logging
    logger = logging.getLogger(__name__)

    catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
    if not catalog:
        raise HTTPException(status_code=404, detail="Catalog not found")

    theme_name = catalog.theme.name if catalog.theme else catalog.name
    existing_names = [asset.name for asset in catalog.assets]

    logger.info(f"[ADD-ASSETS] Adding {count} assets to catalog {catalog_id}")
    logger.info(f"[ADD-ASSETS] Theme: {theme_name}, existing: {len(existing_names)}")

    ollama = OllamaService()
    try:
        new_assets_data = await ollama.generate_additional_assets(
            theme=theme_name,
            existing_names=existing_names,
            count=count
        )
    except Exception as e:
        logger.error(f"[ADD-ASSETS] Ollama generation failed: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to generate assets: {e}")

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
            logger.warning(f"[ADD-ASSETS] Failed to add asset: {asset_data.get('name', 'unknown')} - {e}")

    db.commit()

    logger.info(f"[ADD-ASSETS] Added {len(added_assets)} assets")

    return {
        "message": f"Added {len(added_assets)} assets",
        "catalog_id": catalog_id,
        "added_count": len(added_assets),
    }
