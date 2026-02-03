import asyncio
import logging
import time
from datetime import datetime
import traceback
from pathlib import Path
from sqlalchemy.orm import Session

from backend.config import CATALOGS_DIR
from backend.models.entities import Theme, Catalog, Asset, AssetCategory, GenerationStatus
from backend.models.schemas import ThemeGenerateResponse, AssetListItem, QueueItem, QueueStatus
from backend.services.ollama_service import OllamaService
from backend.services.comfyui_service import ComfyUIService
from backend.services.hunyuan2_service import Hunyuan3DService

logger = logging.getLogger(__name__)


class PipelineService:
    def __init__(self, db: Session):
        self.db = db
        self.ollama = OllamaService()
        self.comfyui = ComfyUIService()
        self.hunyuan3d = Hunyuan3DService()

    async def generate_theme_assets(self, theme_name: str) -> ThemeGenerateResponse:
        """Theme -> Asset list generation pipeline"""
        logger.info(f"Starting theme asset generation: {theme_name}")

        asset_list = await self.ollama.generate_asset_list(theme_name)

        theme = Theme(name=theme_name, description=f"3D assets for '{theme_name}' theme")
        self.db.add(theme)
        self.db.flush()

        catalog = Catalog(
            name=f"{theme_name} Catalog",
            theme_id=theme.id,
            description=f"Asset catalog for '{theme_name}' theme"
        )
        self.db.add(catalog)
        self.db.flush()

        response_assets = []
        for item in asset_list:
            category_str = item.get("category", "other").lower()
            try:
                category = AssetCategory(category_str)
            except ValueError:
                category = AssetCategory.OTHER

            asset = Asset(
                catalog_id=catalog.id,
                name=item.get("name", "unnamed"),
                name_kr=item.get("name_kr", ""),
                category=category,
                description=item.get("description", ""),
                description_kr=item.get("description_kr", ""),
                prompt_2d=item.get("prompt_2d", ""),
                status=GenerationStatus.PENDING,
            )
            self.db.add(asset)

            response_assets.append(AssetListItem(
                name=asset.name,
                name_kr=asset.name_kr,
                category=category.value,
                description=asset.description,
                description_kr=asset.description_kr,
                prompt_2d=asset.prompt_2d,
            ))

        self.db.commit()
        logger.info(f"Created {len(response_assets)} assets")

        return ThemeGenerateResponse(
            theme_id=theme.id,
            catalog_id=catalog.id,
            theme_name=theme_name,
            assets=response_assets,
        )

    async def generate_all_parallel(self, catalog_id: str):
        """
        True parallel pipeline: 2D completes -> immediately start 3D for that asset
        2D and 3D run concurrently
        """
        from backend.api.routes.generation import get_queue, update_queue

        logger.info("=" * 60)
        logger.info(f"[PIPELINE] Starting parallel 2D+3D generation: {catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            raise Exception(f"Catalog not found: {catalog_id}")

        # Get assets that need 2D (no preview image yet)
        assets_need_2d = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.preview_image_path.is_(None),
            Asset.status != GenerationStatus.COMPLETED
        ).all()

        # Get assets that already have 2D but need 3D (not completed yet)
        assets_need_3d = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.preview_image_path.isnot(None),
            Asset.status != GenerationStatus.COMPLETED
        ).all()

        logger.info(f"[PIPELINE] Need 2D: {len(assets_need_2d)}, Already have 2D (need 3D): {len(assets_need_3d)}")

        # Shared queue for 3D processing (assets completed 2D go here)
        pending_3d_queue: asyncio.Queue = asyncio.Queue()

        # Add existing 3D-ready assets to queue
        for asset in assets_need_3d:
            await pending_3d_queue.put((asset.id, asset.name_kr or asset.name))

        # Initialize queue status
        queue = get_queue(catalog_id)
        queue.queue_2d = [
            QueueItem(
                asset_id=a.id,
                asset_name=a.name_kr or a.name,
                queue_type="2d",
                status="pending"
            ) for a in assets_need_2d
        ]
        queue.queue_3d = [
            QueueItem(
                asset_id=a.id,
                asset_name=a.name_kr or a.name,
                queue_type="3d",
                status="pending"
            ) for a in assets_need_3d
        ]
        queue.is_running_2d = len(assets_need_2d) > 0
        queue.is_running_3d = True  # Will run even if empty initially
        update_queue(catalog_id, queue)

        logger.info(f"[PIPELINE] 2D queue: {len(assets_need_2d)}, 3D queue: {len(assets_need_3d)}")

        # 2D Worker
        async def process_2d():
            for idx, asset in enumerate(assets_need_2d):
                asset_id = asset.id
                asset_name = asset.name_kr or asset.name

                logger.info(f"[2D-WORKER] Processing {idx+1}/{len(assets_need_2d)}: {asset_name}")

                # Update queue status
                q = get_queue(catalog_id)
                for item in q.queue_2d:
                    if item.asset_id == asset_id:
                        item.status = "running"
                        item.started_at = datetime.now().isoformat()
                        q.current_2d = item
                        break
                update_queue(catalog_id, q)

                try:
                    await self._generate_2d_only(asset_id)
                    
                    # Update status to completed
                    q = get_queue(catalog_id)
                    for item in q.queue_2d:
                        if item.asset_id == asset_id:
                            item.status = "completed"
                            break
                    q.current_2d = None
                    
                    # Add to 3D queue immediately!
                    new_3d_item = QueueItem(
                        asset_id=asset_id,
                        asset_name=asset_name,
                        queue_type="3d",
                        status="pending"
                    )
                    q.queue_3d.append(new_3d_item)
                    update_queue(catalog_id, q)
                    
                    # Put in async queue for 3D worker
                    await pending_3d_queue.put((asset_id, asset_name))
                    
                    logger.info(f"[2D-WORKER] OK: {asset_name} -> added to 3D queue")

                except Exception as e:
                    q = get_queue(catalog_id)
                    for item in q.queue_2d:
                        if item.asset_id == asset_id:
                            item.status = "failed"
                            item.error = str(e)
                            break
                    q.current_2d = None
                    update_queue(catalog_id, q)
                    logger.error(f"[2D-WORKER] FAIL: {asset_name} - {e}")

            # Mark 2D as done
            q = get_queue(catalog_id)
            q.is_running_2d = False
            q.current_2d = None
            update_queue(catalog_id, q)
            
            # Signal 3D worker that no more items coming
            await pending_3d_queue.put(None)
            logger.info("[2D-WORKER] Completed all 2D tasks")

        # 3D Worker
        async def process_3d():
            processed = 0
            while True:
                try:
                    # Wait for item with timeout
                    item = await asyncio.wait_for(pending_3d_queue.get(), timeout=2.0)
                except asyncio.TimeoutError:
                    # Check if 2D is still running
                    q = get_queue(catalog_id)
                    if not q.is_running_2d and pending_3d_queue.empty():
                        break
                    continue

                if item is None:
                    # 2D worker signaled completion
                    # But there might still be items in queue
                    if pending_3d_queue.empty():
                        break
                    continue

                asset_id, asset_name = item
                processed += 1
                logger.info(f"[3D-WORKER] Processing: {asset_name}")

                # Update queue status
                q = get_queue(catalog_id)
                for qi in q.queue_3d:
                    if qi.asset_id == asset_id:
                        qi.status = "running"
                        qi.started_at = datetime.now().isoformat()
                        q.current_3d = qi
                        break
                update_queue(catalog_id, q)

                try:
                    await self._generate_3d_only(asset_id)
                    
                    q = get_queue(catalog_id)
                    for qi in q.queue_3d:
                        if qi.asset_id == asset_id:
                            qi.status = "completed"
                            break
                    q.current_3d = None
                    update_queue(catalog_id, q)
                    
                    logger.info(f"[3D-WORKER] OK: {asset_name}")

                except Exception as e:
                    q = get_queue(catalog_id)
                    for qi in q.queue_3d:
                        if qi.asset_id == asset_id:
                            qi.status = "failed"
                            qi.error = str(e)
                            break
                    q.current_3d = None
                    update_queue(catalog_id, q)
                    logger.error(f"[3D-WORKER] FAIL: {asset_name} - {e}")

            # Mark 3D as done
            q = get_queue(catalog_id)
            q.is_running_3d = False
            q.current_3d = None
            update_queue(catalog_id, q)
            logger.info(f"[3D-WORKER] Completed all 3D tasks (processed: {processed})")

        # Run both workers concurrently!
        await asyncio.gather(process_2d(), process_3d())

        logger.info("[PIPELINE] All parallel generation complete!")
        logger.info("=" * 60)

    async def generate_2d_batch(self, catalog_id: str):
        """Batch 2D image generation only"""
        from backend.api.routes.generation import get_queue, update_queue

        logger.info("=" * 60)
        logger.info(f"[BATCH-2D] Starting 2D batch: catalog_id={catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            raise Exception(f"Catalog not found: {catalog_id}")

        assets = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            (Asset.status.in_([GenerationStatus.PENDING, GenerationStatus.FAILED])) |
            (Asset.preview_image_path.is_(None))
        ).all()

        if not assets:
            logger.warning("[BATCH-2D] No assets to generate")
            return

        queue = get_queue(catalog_id)
        queue.queue_2d = [
            QueueItem(
                asset_id=a.id,
                asset_name=a.name_kr or a.name,
                queue_type="2d",
                status="pending"
            ) for a in assets
        ]
        queue.is_running_2d = True
        queue.current_2d = None
        update_queue(catalog_id, queue)

        for idx, asset in enumerate(assets):
            asset_id = asset.id
            asset_name = asset.name_kr or asset.name

            logger.info(f"[BATCH-2D] {idx+1}/{len(assets)}: {asset_name}")

            q = get_queue(catalog_id)
            for item in q.queue_2d:
                if item.asset_id == asset_id:
                    item.status = "running"
                    item.started_at = datetime.now().isoformat()
                    q.current_2d = item
                    break
            update_queue(catalog_id, q)

            try:
                await self._generate_2d_only(asset_id)
                q = get_queue(catalog_id)
                for item in q.queue_2d:
                    if item.asset_id == asset_id:
                        item.status = "completed"
                        break
                q.current_2d = None
                update_queue(catalog_id, q)
                logger.info(f"[BATCH-2D] OK: {asset_name}")
            except Exception as e:
                q = get_queue(catalog_id)
                for item in q.queue_2d:
                    if item.asset_id == asset_id:
                        item.status = "failed"
                        item.error = str(e)
                        break
                q.current_2d = None
                update_queue(catalog_id, q)
                logger.error(f"[BATCH-2D] FAIL: {asset_name} - {e}")

        queue = get_queue(catalog_id)
        queue.is_running_2d = False
        queue.current_2d = None
        update_queue(catalog_id, queue)
        logger.info("[BATCH-2D] Complete")
        logger.info("=" * 60)

    async def generate_3d_batch(self, catalog_id: str):
        """Batch 3D model generation only"""
        from backend.api.routes.generation import get_queue, update_queue

        logger.info("=" * 60)
        logger.info(f"[BATCH-3D] Starting 3D batch: catalog_id={catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            raise Exception(f"Catalog not found: {catalog_id}")

        assets = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.status.in_([GenerationStatus.GENERATING_3D, GenerationStatus.FAILED]),
            Asset.preview_image_path.isnot(None)
        ).all()

        if not assets:
            logger.warning("[BATCH-3D] No assets to generate (need 2D images first)")
            return

        queue = get_queue(catalog_id)
        queue.queue_3d = [
            QueueItem(
                asset_id=a.id,
                asset_name=a.name_kr or a.name,
                queue_type="3d",
                status="pending"
            ) for a in assets
        ]
        queue.is_running_3d = True
        queue.current_3d = None
        update_queue(catalog_id, queue)

        for idx, asset in enumerate(assets):
            asset_id = asset.id
            asset_name = asset.name_kr or asset.name

            logger.info(f"[BATCH-3D] {idx+1}/{len(assets)}: {asset_name}")

            q = get_queue(catalog_id)
            for item in q.queue_3d:
                if item.asset_id == asset_id:
                    item.status = "running"
                    item.started_at = datetime.now().isoformat()
                    q.current_3d = item
                    break
            update_queue(catalog_id, q)

            try:
                await self._generate_3d_only(asset_id)
                q = get_queue(catalog_id)
                for item in q.queue_3d:
                    if item.asset_id == asset_id:
                        item.status = "completed"
                        break
                q.current_3d = None
                update_queue(catalog_id, q)
                logger.info(f"[BATCH-3D] OK: {asset_name}")
            except Exception as e:
                q = get_queue(catalog_id)
                for item in q.queue_3d:
                    if item.asset_id == asset_id:
                        item.status = "failed"
                        item.error = str(e)
                        break
                q.current_3d = None
                update_queue(catalog_id, q)
                logger.error(f"[BATCH-3D] FAIL: {asset_name} - {e}")

        queue = get_queue(catalog_id)
        queue.is_running_3d = False
        queue.current_3d = None
        update_queue(catalog_id, queue)
        logger.info("[BATCH-3D] Complete")
        logger.info("=" * 60)

    async def _generate_2d_only(self, asset_id: str):
        """Generate 2D image for single asset"""
        asset = self.db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            raise Exception(f"Asset not found: {asset_id}")

        catalog_id = asset.catalog_id
        asset_dir = CATALOGS_DIR / catalog_id / "assets" / asset_id
        asset_dir.mkdir(parents=True, exist_ok=True)

        try:
            asset.status = GenerationStatus.GENERATING_2D
            self.db.commit()

            preview_path = asset_dir / "preview.png"
            await self.comfyui.generate_image(asset.prompt_2d, preview_path)

            asset.preview_image_path = str(preview_path)
            asset.status = GenerationStatus.GENERATING_3D
            asset.error_message = None
            self.db.commit()

            return asset

        except Exception as e:
            asset.status = GenerationStatus.FAILED
            asset.error_message = str(e)
            self.db.commit()
            raise

    async def _generate_3d_only(self, asset_id: str):
        """Generate 3D model for single asset"""
        asset = self.db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            raise Exception(f"Asset not found: {asset_id}")

        if not asset.preview_image_path:
            raise Exception(f"No 2D image: {asset.name}")

        preview_path = Path(asset.preview_image_path)
        if not preview_path.exists():
            raise Exception(f"2D image file missing: {preview_path}")

        catalog_id = asset.catalog_id
        asset_dir = CATALOGS_DIR / catalog_id / "assets" / asset_id

        try:
            asset.status = GenerationStatus.GENERATING_3D
            self.db.commit()

            result = await self.hunyuan3d.generate_3d_from_image(
                str(preview_path),
                asset_dir,
                asset_id,
                enable_texture=True,
            )

            asset.model_glb_path = result.get("glb_path")
            asset.model_obj_path = result.get("obj_path")
            asset.status = GenerationStatus.COMPLETED
            asset.error_message = None
            self.db.commit()

            return asset

        except Exception as e:
            asset.status = GenerationStatus.FAILED
            asset.error_message = str(e)
            self.db.commit()
            raise
