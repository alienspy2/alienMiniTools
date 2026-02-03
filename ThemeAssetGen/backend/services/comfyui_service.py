import asyncio
import json
import random
import time
import logging
import aiohttp
from pathlib import Path

from backend.config import COMFYUI_URL, COMFYUI_WORKFLOW_PATH, COMFYUI_TIMEOUT

logger = logging.getLogger(__name__)


class ComfyUIService:
    def __init__(self):
        self.base_url = COMFYUI_URL
        self.workflow_path = COMFYUI_WORKFLOW_PATH
        self.timeout = aiohttp.ClientTimeout(total=COMFYUI_TIMEOUT)
        self.poll_interval = 2  # seconds

    def load_workflow(self) -> dict:
        """Load workflow JSON (zit_assetgen_api.json)"""
        with open(self.workflow_path, "r", encoding="utf-8") as f:
            return json.load(f)

    def set_prompt(self, workflow: dict, prompt: str) -> bool:
        """Set prompt in node 58
        
        Node 58 is PrimitiveStringMultiline that holds the user prompt.
        Style prompt is already embedded in node 61 (StringConcatenate).
        """
        if "58" in workflow:
            workflow["58"].setdefault("inputs", {})["value"] = prompt
            return True
        return False

    def set_random_seed(self, workflow: dict) -> int:
        """워크플로우의 시드를 랜덤하게 설정 (캐싱 방지)"""
        seed = random.randint(1, 2**31 - 1)
        for node in workflow.values():
            if node.get("class_type") == "KSampler":
                node.setdefault("inputs", {})["seed"] = seed
                return seed
        return seed

    def enhance_prompt_for_isolation(self, prompt: str) -> str:
        """프롬프트에 배경 제거 키워드 추가"""
        # 이미 배경 관련 키워드가 있는지 확인
        isolation_keywords = [
            "white background", "isolated", "no background",
            "pure white", "solid white", "transparent background"
        ]
        has_isolation = any(kw in prompt.lower() for kw in isolation_keywords)

        if not has_isolation:
            # 배경 제거 키워드 추가
            prompt = f"{prompt}, isolated on pure white background, no shadow, no floor, single object centered, product photography, studio lighting"

        return prompt

    async def check_health(self) -> bool:
        """ComfyUI 서버 상태 확인"""
        try:
            async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=5)) as session:
                async with session.get(f"{self.base_url}/system_stats") as response:
                    return response.status == 200
        except Exception:
            return False

    async def generate_image(self, prompt: str, output_path: Path) -> str:
        """Generate 2D image using zit_assetgen_api.json
        
        Uses node 58 for prompt input. Style prompt is already embedded in the API.
        """
        logger.info(f"[COMFYUI] Image generation started")
        logger.info(f"[COMFYUI] Server: {self.base_url}")
        logger.debug(f"[COMFYUI] Prompt: {prompt}")
        logger.debug(f"[COMFYUI] Output: {output_path}")

        # Load workflow (style prompt is already embedded)
        try:
            workflow = self.load_workflow()
            logger.debug(f"[COMFYUI] Workflow loaded: {self.workflow_path}")
        except FileNotFoundError:
            logger.error(f"[COMFYUI] Workflow file not found: {self.workflow_path}")
            raise Exception(f"Workflow file not found: {self.workflow_path}")

        # Set prompt in node 58 (no additional style prompt needed)
        if not self.set_prompt(workflow, prompt):
            logger.warning("[COMFYUI] Node 58 not found in workflow")

        # Set random seed (prevent caching)
        seed = self.set_random_seed(workflow)
        logger.debug(f"[COMFYUI] Seed: {seed}")

        try:
            async with aiohttp.ClientSession(timeout=self.timeout) as session:
                # 프롬프트 큐에 추가
                logger.debug(f"[COMFYUI] 프롬프트 큐 등록 중...")
                async with session.post(
                    f"{self.base_url}/prompt",
                    json={"prompt": workflow}
                ) as response:
                    if response.status != 200:
                        error = await response.text()
                        logger.error(f"[COMFYUI] 큐 등록 실패: {error}")
                        raise Exception(f"ComfyUI queue failed: {error}")

                    queued = await response.json()
                    prompt_id = queued.get("prompt_id")

                if not prompt_id:
                    logger.error("[COMFYUI] prompt_id 없음")
                    raise Exception("ComfyUI did not return prompt_id")

                logger.info(f"[COMFYUI] prompt_id: {prompt_id}")

                # 완료 대기
                deadline = time.time() + COMFYUI_TIMEOUT
                poll_count = 0
                while time.time() < deadline:
                    poll_count += 1
                    async with session.get(f"{self.base_url}/history/{prompt_id}") as response:
                        history = await response.json()
                        entry = history.get(prompt_id)
                        if entry:
                            # outputs가 있거나 status.completed가 true면 완료
                            has_outputs = bool(entry.get("outputs"))
                            status = entry.get("status", {})
                            is_completed = status.get("completed", False)

                            if has_outputs or is_completed:
                                logger.debug(f"[COMFYUI] 완료 (폴링 {poll_count}회, outputs={has_outputs}, completed={is_completed})")
                                break

                    if poll_count % 10 == 0:
                        logger.debug(f"[COMFYUI] 대기 중... ({poll_count}회 폴링)")
                    await asyncio.sleep(self.poll_interval)
                else:
                    logger.error(f"[COMFYUI] 타임아웃 (폴링 {poll_count}회)")
                    raise Exception("ComfyUI generation timeout")

                # 이미지 다운로드
                images = []
                outputs = entry.get("outputs", {})
                logger.debug(f"[COMFYUI] 출력 노드: {list(outputs.keys())}")

                for node_id, node_data in outputs.items():
                    for image in node_data.get("images", []):
                        filename = image.get("filename")
                        if filename:
                            images.append({
                                "filename": filename,
                                "subfolder": image.get("subfolder", ""),
                                "type": image.get("type", "output"),
                            })
                            logger.debug(f"[COMFYUI] 이미지 발견: {filename}")

                # 캐시된 경우 outputs가 비어있을 수 있음 - 최근 output 파일 검색
                if not images and is_completed:
                    logger.warning("[COMFYUI] outputs 비어있음 (캐시됨), output 폴더에서 검색")
                    # ComfyUI output 폴더에서 최근 파일 검색 시도
                    try:
                        async with session.get(f"{self.base_url}/view?filename=asset_00001_.png&type=output") as test_response:
                            if test_response.status == 200:
                                images.append({
                                    "filename": "asset_00001_.png",
                                    "subfolder": "",
                                    "type": "output",
                                })
                                logger.debug("[COMFYUI] 기본 output 파일 발견")
                    except Exception:
                        pass

                if not images:
                    logger.error("[COMFYUI] 생성된 이미지 없음")
                    raise Exception("No images generated")

                logger.info(f"[COMFYUI] 생성된 이미지: {len(images)}개")

                # 첫 번째 이미지 다운로드
                img = images[0]
                url = (
                    f"{self.base_url}/view?filename={img['filename']}"
                    f"&subfolder={img['subfolder']}&type={img['type']}"
                )
                logger.debug(f"[COMFYUI] 다운로드 URL: {url}")

                async with session.get(url) as response:
                    if response.status != 200:
                        logger.error(f"[COMFYUI] 다운로드 실패: status={response.status}")
                        raise Exception("Failed to download image")

                    output_path.parent.mkdir(parents=True, exist_ok=True)
                    content = await response.read()
                    with open(output_path, "wb") as f:
                        f.write(content)

                    logger.info(f"[COMFYUI] 이미지 저장: {output_path} ({len(content)} bytes)")

                return str(output_path)

        except aiohttp.ClientError as e:
            logger.error(f"[COMFYUI] 네트워크 에러: {e}")
            raise Exception(f"ComfyUI connection error: {e}")
