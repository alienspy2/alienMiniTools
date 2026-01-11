import logging
import shutil
import traceback
from pathlib import Path

from backend.config import HUNYUAN3D_URL, HUNYUAN3D_TIMEOUT

logger = logging.getLogger(__name__)


class Hunyuan3DService:
    def __init__(self):
        self.base_url = HUNYUAN3D_URL
        self.timeout = HUNYUAN3D_TIMEOUT

    async def check_health(self) -> bool:
        """서버 상태 확인"""
        try:
            from gradio_client import Client
            client = Client(self.base_url, verbose=False)
            return True
        except Exception:
            return False

    async def generate_3d_from_image(
        self,
        image_path: str,
        output_dir: Path,
        asset_id: str,
        enable_texture: bool = True,
    ) -> dict:
        """2D 이미지에서 3D 모델 생성 (Gradio Client 사용)"""
        from gradio_client import Client, handle_file
        import asyncio

        logger.info(f"Hunyuan3D 3D 생성 시작: {image_path}")
        logger.debug(f"  output_dir: {output_dir}")
        logger.debug(f"  asset_id: {asset_id}")
        logger.debug(f"  enable_texture: {enable_texture}")
        logger.debug(f"  server_url: {self.base_url}")

        output_dir.mkdir(parents=True, exist_ok=True)
        glb_path = output_dir / f"{asset_id}.glb"

        def _generate():
            client = Client(self.base_url, verbose=False)

            if enable_texture:
                # generation_all: shape + texture
                result = client.predict(
                    caption=None,
                    image=handle_file(image_path),
                    mv_image_front=None,
                    mv_image_back=None,
                    mv_image_left=None,
                    mv_image_right=None,
                    steps=50,
                    guidance_scale=7.5,
                    seed=1234,
                    octree_resolution=256,
                    check_box_rembg=True,
                    num_chunks=200000,
                    randomize_seed=False,
                    api_name="/generation_all"
                )
                # result: (path, path_textured, html, stats, seed)
                # 텍스처가 있는 버전 사용
                generated_path = result[1]
            else:
                # shape_generation: shape only
                result = client.predict(
                    caption=None,
                    image=handle_file(image_path),
                    mv_image_front=None,
                    mv_image_back=None,
                    mv_image_left=None,
                    mv_image_right=None,
                    steps=50,
                    guidance_scale=7.5,
                    seed=1234,
                    octree_resolution=256,
                    check_box_rembg=True,
                    num_chunks=200000,
                    randomize_seed=False,
                    api_name="/shape_generation"
                )
                # result: (path, html, stats, seed)
                generated_path = result[0]

            return generated_path

        # 동기 함수를 비동기로 실행
        try:
            loop = asyncio.get_event_loop()
            logger.debug("Gradio client 호출 시작...")
            generated_path = await loop.run_in_executor(None, _generate)
            logger.debug(f"Gradio client 응답: {generated_path}")

            # Gradio client가 dict를 반환할 수 있음 (새 버전)
            if isinstance(generated_path, dict):
                generated_path = generated_path.get("value", generated_path.get("path"))
                logger.debug(f"Dict에서 추출한 경로: {generated_path}")

        except Exception as e:
            logger.error(f"Hunyuan3D API 호출 실패: {e}")
            logger.debug(traceback.format_exc())
            raise

        # 생성된 파일을 출력 디렉토리로 복사
        if generated_path and Path(generated_path).exists():
            shutil.copy(generated_path, glb_path)
            logger.info(f"GLB 저장 완료: {glb_path}")
        else:
            logger.error(f"생성된 파일을 찾을 수 없음: {generated_path}")
            raise Exception(f"생성된 파일을 찾을 수 없음: {generated_path}")

        # OBJ 변환
        obj_path = await self._convert_to_obj(glb_path)

        return {
            "glb_path": str(glb_path),
            "obj_path": str(obj_path) if obj_path else None,
        }

    async def _convert_to_obj(self, glb_path: Path) -> Path | None:
        """GLB를 OBJ로 변환"""
        try:
            import trimesh
            mesh = trimesh.load(str(glb_path))
            obj_path = glb_path.with_suffix(".obj")
            mesh.export(str(obj_path), file_type="obj")
            logger.info(f"OBJ 변환 완료: {obj_path}")
            return obj_path
        except ImportError:
            logger.warning("trimesh가 설치되지 않아 OBJ 변환 불가")
            return None
        except Exception as e:
            logger.error(f"OBJ 변환 실패: {e}")
            return None
