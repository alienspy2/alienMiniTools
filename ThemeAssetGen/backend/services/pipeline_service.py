import logging
import time
import traceback
from pathlib import Path
from sqlalchemy.orm import Session

from backend.config import CATALOGS_DIR
from backend.models.entities import Theme, Catalog, Asset, AssetCategory, GenerationStatus
from backend.models.schemas import ThemeGenerateResponse, AssetListItem, BatchGenerationStatus
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
        """테마 → 에셋 리스트 생성 파이프라인"""
        logger.info(f"테마 에셋 생성 시작: {theme_name}")

        # 1. Ollama로 에셋 리스트 생성
        asset_list = await self.ollama.generate_asset_list(theme_name)

        # 2. Theme 생성
        theme = Theme(name=theme_name, description=f"'{theme_name}' 테마의 3D 에셋")
        self.db.add(theme)
        self.db.flush()

        # 3. Catalog 생성
        catalog = Catalog(
            name=f"{theme_name} 카탈로그",
            theme_id=theme.id,
            description=f"'{theme_name}' 테마를 위한 에셋 카탈로그"
        )
        self.db.add(catalog)
        self.db.flush()

        # 4. Asset 엔티티 생성
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
                prompt_2d=item.get("prompt_2d", ""),
                status=GenerationStatus.PENDING,
            )
            self.db.add(asset)

            response_assets.append(AssetListItem(
                name=asset.name,
                name_kr=asset.name_kr,
                category=category.value,
                description=asset.description,
                prompt_2d=asset.prompt_2d,
            ))

        self.db.commit()
        logger.info(f"에셋 {len(response_assets)}개 생성 완료")

        return ThemeGenerateResponse(
            theme_id=theme.id,
            catalog_id=catalog.id,
            theme_name=theme_name,
            assets=response_assets,
        )

    async def generate_single_asset(self, asset_id: str) -> Asset:
        """단일 에셋 생성 파이프라인 (2D → 3D)"""
        logger.info(f"=" * 60)
        logger.info(f"[PIPELINE] 단일 에셋 생성 시작: {asset_id}")

        asset = self.db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            logger.error(f"[PIPELINE] 에셋을 찾을 수 없음: {asset_id}")
            raise Exception(f"Asset not found: {asset_id}")

        logger.info(f"[PIPELINE] 에셋 정보:")
        logger.info(f"  - 이름: {asset.name_kr or asset.name}")
        logger.info(f"  - 카테고리: {asset.category.value}")
        logger.info(f"  - 2D 프롬프트: {asset.prompt_2d[:100]}...")

        catalog_id = asset.catalog_id
        asset_dir = CATALOGS_DIR / catalog_id / "assets" / asset_id
        asset_dir.mkdir(parents=True, exist_ok=True)
        logger.debug(f"[PIPELINE] 출력 디렉토리: {asset_dir}")

        try:
            # 1. 2D 이미지 생성
            logger.info(f"[PIPELINE] === 1단계: 2D 이미지 생성 ===")
            asset.status = GenerationStatus.GENERATING_2D
            self.db.commit()

            preview_path = asset_dir / "preview.png"
            logger.info(f"[PIPELINE] ComfyUI 호출 시작...")
            start_time = time.time()

            await self.comfyui.generate_image(asset.prompt_2d, preview_path)

            elapsed = time.time() - start_time
            logger.info(f"[PIPELINE] ComfyUI 완료: {elapsed:.1f}초")
            logger.info(f"[PIPELINE] 2D 이미지 저장: {preview_path}")

            asset.preview_image_path = str(preview_path)
            self.db.commit()

            # 파일 존재 확인
            if not preview_path.exists():
                raise Exception(f"2D 이미지 파일이 생성되지 않음: {preview_path}")
            logger.info(f"[PIPELINE] 2D 이미지 크기: {preview_path.stat().st_size} bytes")

            # 2. 3D 모델 생성
            logger.info(f"[PIPELINE] === 2단계: 3D 모델 생성 ===")
            asset.status = GenerationStatus.GENERATING_3D
            self.db.commit()

            logger.info(f"[PIPELINE] Hunyuan3D 호출 시작...")
            start_time = time.time()

            result = await self.hunyuan3d.generate_3d_from_image(
                str(preview_path),
                asset_dir,
                asset_id,
                enable_texture=True,
            )

            elapsed = time.time() - start_time
            logger.info(f"[PIPELINE] Hunyuan3D 완료: {elapsed:.1f}초")
            logger.info(f"[PIPELINE] 결과: {result}")

            asset.model_glb_path = result.get("glb_path")
            asset.model_obj_path = result.get("obj_path")
            asset.status = GenerationStatus.COMPLETED
            asset.error_message = None  # 성공 시 에러 메시지 삭제
            self.db.commit()

            logger.info(f"[PIPELINE] 에셋 생성 완료: {asset.name}")
            logger.info(f"=" * 60)
            return asset

        except Exception as e:
            logger.error(f"[PIPELINE] 에셋 생성 실패: {asset.name}")
            logger.error(f"[PIPELINE] 에러: {e}")
            logger.debug(f"[PIPELINE] 스택 트레이스:\n{traceback.format_exc()}")
            asset.status = GenerationStatus.FAILED
            asset.error_message = str(e)
            self.db.commit()
            raise

    async def generate_batch(self, catalog_id: str, asset_ids: list[str] = None):
        """배치 에셋 생성"""
        from backend.api.routes.generation import set_status

        logger.info(f"=" * 60)
        logger.info(f"[BATCH] 배치 생성 시작: catalog_id={catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            logger.error(f"[BATCH] 카탈로그를 찾을 수 없음: {catalog_id}")
            raise Exception(f"Catalog not found: {catalog_id}")

        logger.info(f"[BATCH] 카탈로그: {catalog.name}")

        # 생성할 에셋 목록
        if asset_ids:
            logger.info(f"[BATCH] 지정된 에셋 ID: {len(asset_ids)}개")
            assets = self.db.query(Asset).filter(
                Asset.catalog_id == catalog_id,
                Asset.id.in_(asset_ids)
            ).all()
        else:
            logger.info(f"[BATCH] 미완료 에셋 전체 조회")
            assets = self.db.query(Asset).filter(
                Asset.catalog_id == catalog_id,
                Asset.status != GenerationStatus.COMPLETED
            ).all()

        total = len(assets)
        completed = 0
        failed = 0

        logger.info(f"[BATCH] 생성할 에셋: {total}개")
        for i, a in enumerate(assets):
            logger.debug(f"[BATCH]   {i+1}. {a.name_kr or a.name} [{a.status.value}]")

        # 초기 상태 설정
        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=0,
            failed=0,
            current_asset=None,
            current_status="starting",
        ))

        # 에셋 정보를 미리 저장 (세션 분리 문제 방지)
        asset_info = [(a.id, a.name_kr or a.name, a.name) for a in assets]
        batch_start_time = time.time()

        for idx, (asset_id, asset_display_name, asset_name) in enumerate(asset_info):
            logger.info(f"[BATCH] --- 에셋 {idx+1}/{total}: {asset_display_name} ---")

            set_status(catalog_id, BatchGenerationStatus(
                catalog_id=catalog_id,
                total=total,
                completed=completed,
                failed=failed,
                current_asset=asset_display_name,
                current_status="generating",
            ))

            try:
                await self.generate_single_asset(asset_id)
                completed += 1
                logger.info(f"[BATCH] ✅ 성공: {asset_display_name}")
            except Exception as e:
                logger.error(f"[BATCH] ❌ 실패: {asset_name}")
                logger.error(f"[BATCH] 에러: {e}")
                logger.debug(f"[BATCH] 스택:\n{traceback.format_exc()}")
                failed += 1

            logger.info(f"[BATCH] 진행: {completed + failed}/{total} (성공: {completed}, 실패: {failed})")

        # 완료 상태
        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=completed,
            failed=failed,
            current_asset=None,
            current_status="completed",
        ))

        batch_elapsed = time.time() - batch_start_time
        logger.info(f"[BATCH] 배치 생성 완료")
        logger.info(f"[BATCH] 총 소요시간: {batch_elapsed:.1f}초")
        logger.info(f"[BATCH] 결과: 성공={completed}, 실패={failed}")
        logger.info(f"=" * 60)

    async def generate_2d_batch(self, catalog_id: str):
        """배치 2D 이미지만 생성"""
        from backend.api.routes.generation import set_status

        logger.info(f"=" * 60)
        logger.info(f"[BATCH-2D] 2D 배치 생성 시작: catalog_id={catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            raise Exception(f"Catalog not found: {catalog_id}")

        # 2D 이미지가 없는 에셋만 조회 (pending 또는 failed)
        assets = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.status.in_([GenerationStatus.PENDING, GenerationStatus.FAILED])
        ).all()

        total = len(assets)
        completed = 0
        failed = 0

        logger.info(f"[BATCH-2D] 생성할 에셋: {total}개")

        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=0,
            failed=0,
            current_asset=None,
            current_status="starting",
        ))

        asset_info = [(a.id, a.name_kr or a.name) for a in assets]
        batch_start_time = time.time()

        for idx, (asset_id, asset_display_name) in enumerate(asset_info):
            logger.info(f"[BATCH-2D] --- 에셋 {idx+1}/{total}: {asset_display_name} ---")

            set_status(catalog_id, BatchGenerationStatus(
                catalog_id=catalog_id,
                total=total,
                completed=completed,
                failed=failed,
                current_asset=f"{asset_display_name} (2D)",
                current_status="generating",
            ))

            try:
                await self._generate_2d_only(asset_id)
                completed += 1
                logger.info(f"[BATCH-2D] ✅ 성공: {asset_display_name}")
            except Exception as e:
                logger.error(f"[BATCH-2D] ❌ 실패: {asset_display_name} - {e}")
                failed += 1

        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=completed,
            failed=failed,
            current_asset=None,
            current_status="completed",
        ))

        batch_elapsed = time.time() - batch_start_time
        logger.info(f"[BATCH-2D] 완료: {batch_elapsed:.1f}초, 성공={completed}, 실패={failed}")
        logger.info(f"=" * 60)

    async def _generate_2d_only(self, asset_id: str):
        """단일 에셋 2D 이미지만 생성"""
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
            asset.status = GenerationStatus.GENERATING_3D  # 2D 완료, 3D 대기 상태
            asset.error_message = None
            self.db.commit()

            logger.info(f"[2D] 생성 완료: {asset.name}")
            return asset

        except Exception as e:
            asset.status = GenerationStatus.FAILED
            asset.error_message = str(e)
            self.db.commit()
            raise

    async def generate_3d_batch(self, catalog_id: str):
        """배치 3D 모델만 생성 (2D 이미지가 있는 에셋만)"""
        from backend.api.routes.generation import set_status

        logger.info(f"=" * 60)
        logger.info(f"[BATCH-3D] 3D 배치 생성 시작: catalog_id={catalog_id}")

        catalog = self.db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            raise Exception(f"Catalog not found: {catalog_id}")

        # 2D 이미지가 있고 3D가 아직 없는 에셋 (generating_3d 상태)
        assets = self.db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.status == GenerationStatus.GENERATING_3D,
            Asset.preview_image_path.isnot(None)
        ).all()

        total = len(assets)
        completed = 0
        failed = 0

        logger.info(f"[BATCH-3D] 생성할 에셋: {total}개")

        if total == 0:
            logger.warning(f"[BATCH-3D] 3D 생성할 에셋이 없습니다. 먼저 2D를 생성하세요.")
            set_status(catalog_id, BatchGenerationStatus(
                catalog_id=catalog_id,
                total=0,
                completed=0,
                failed=0,
                current_asset=None,
                current_status="completed",
            ))
            return

        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=0,
            failed=0,
            current_asset=None,
            current_status="starting",
        ))

        asset_info = [(a.id, a.name_kr or a.name) for a in assets]
        batch_start_time = time.time()

        for idx, (asset_id, asset_display_name) in enumerate(asset_info):
            logger.info(f"[BATCH-3D] --- 에셋 {idx+1}/{total}: {asset_display_name} ---")

            set_status(catalog_id, BatchGenerationStatus(
                catalog_id=catalog_id,
                total=total,
                completed=completed,
                failed=failed,
                current_asset=f"{asset_display_name} (3D)",
                current_status="generating",
            ))

            try:
                await self._generate_3d_only(asset_id)
                completed += 1
                logger.info(f"[BATCH-3D] ✅ 성공: {asset_display_name}")
            except Exception as e:
                logger.error(f"[BATCH-3D] ❌ 실패: {asset_display_name} - {e}")
                failed += 1

        set_status(catalog_id, BatchGenerationStatus(
            catalog_id=catalog_id,
            total=total,
            completed=completed,
            failed=failed,
            current_asset=None,
            current_status="completed",
        ))

        batch_elapsed = time.time() - batch_start_time
        logger.info(f"[BATCH-3D] 완료: {batch_elapsed:.1f}초, 성공={completed}, 실패={failed}")
        logger.info(f"=" * 60)

    async def _generate_3d_only(self, asset_id: str):
        """단일 에셋 3D 모델만 생성"""
        asset = self.db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            raise Exception(f"Asset not found: {asset_id}")

        if not asset.preview_image_path:
            raise Exception(f"2D 이미지가 없습니다: {asset.name}")

        preview_path = Path(asset.preview_image_path)
        if not preview_path.exists():
            raise Exception(f"2D 이미지 파일이 없습니다: {preview_path}")

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

            logger.info(f"[3D] 생성 완료: {asset.name}")
            return asset

        except Exception as e:
            asset.status = GenerationStatus.FAILED
            asset.error_message = str(e)
            self.db.commit()
            raise
