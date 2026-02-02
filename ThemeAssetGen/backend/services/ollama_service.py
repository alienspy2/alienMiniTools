import json
import logging
import aiohttp

from backend.config import OLLAMA_URL, OLLAMA_MODEL, OLLAMA_TIMEOUT
from backend.utils.prompt_templates import ASSET_LIST_PROMPT, ADDITIONAL_ASSETS_PROMPT

logger = logging.getLogger(__name__)


class OllamaService:
    def __init__(self):
        self.base_url = OLLAMA_URL
        self.model = OLLAMA_MODEL
        self.timeout = aiohttp.ClientTimeout(total=OLLAMA_TIMEOUT)

    async def check_health(self) -> bool:
        """Ollama 서버 상태 확인"""
        try:
            async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=5)) as session:
                async with session.get(f"{self.base_url}/api/tags") as response:
                    return response.status == 200
        except Exception:
            return False

    async def generate_asset_list(self, theme: str) -> list[dict]:
        """테마를 입력받아 에셋 리스트 생성"""
        prompt = ASSET_LIST_PROMPT.format(theme=theme)

        payload = {
            "model": self.model,
            "prompt": prompt,
            "stream": False,
        }

        logger.info(f"Ollama 요청: theme={theme}, model={self.model}")

        async with aiohttp.ClientSession(timeout=self.timeout) as session:
            async with session.post(
                f"{self.base_url}/api/generate",
                json=payload
            ) as response:
                if response.status != 200:
                    error = await response.text()
                    logger.error(f"Ollama 에러: {error}")
                    raise Exception(f"Ollama error: {error}")

                data = await response.json()
                raw_response = data.get("response", "")

        # JSON 파싱
        try:
            # JSON 블록 추출
            json_str = raw_response
            if "```json" in json_str:
                json_str = json_str.split("```json")[1].split("```")[0]
            elif "```" in json_str:
                json_str = json_str.split("```")[1].split("```")[0]

            parsed = json.loads(json_str.strip())
            assets = parsed.get("assets", [])
            logger.info(f"에셋 리스트 생성 완료: {len(assets)}개")
            return assets

        except json.JSONDecodeError as e:
            logger.error(f"JSON 파싱 실패: {e}\n응답: {raw_response[:500]}")
            raise Exception(f"Failed to parse asset list: {e}")

    async def refine_prompt(self, prompt: str) -> str:
        """프롬프트를 이미지 생성에 최적화"""
        instructions = (
            "Enhance this prompt for 3D asset image generation. "
            "The object should be isolated on white background, single object only, "
            "good lighting for 3D reconstruction. Output ONLY the enhanced prompt."
        )

        payload = {
            "model": self.model,
            "prompt": f"{instructions}\n\nOriginal: {prompt}",
            "stream": False,
        }

        async with aiohttp.ClientSession(timeout=self.timeout) as session:
            async with session.post(
                f"{self.base_url}/api/generate",
                json=payload
            ) as response:
                if response.status != 200:
                    return prompt  # 실패 시 원본 반환

                data = await response.json()
                refined = data.get("response", "").strip()
                return refined if refined else prompt

    async def generate_additional_assets(self, theme: str, existing_names: list[str], count: int = 10) -> list[dict]:
        """기존 에셋을 제외한 추가 에셋 리스트 생성"""
        existing_assets_str = ", ".join(existing_names) if existing_names else "없음"
        prompt = ADDITIONAL_ASSETS_PROMPT.format(
            theme=theme,
            existing_assets=existing_assets_str,
            count=count
        )

        payload = {
            "model": self.model,
            "prompt": prompt,
            "stream": False,
        }

        logger.info(f"Ollama 추가 에셋 요청: theme={theme}, count={count}, existing={len(existing_names)}개")

        async with aiohttp.ClientSession(timeout=self.timeout) as session:
            async with session.post(
                f"{self.base_url}/api/generate",
                json=payload
            ) as response:
                if response.status != 200:
                    error = await response.text()
                    logger.error(f"Ollama 에러: {error}")
                    raise Exception(f"Ollama error: {error}")

                data = await response.json()
                raw_response = data.get("response", "")

        # JSON 파싱
        try:
            json_str = raw_response
            if "```json" in json_str:
                json_str = json_str.split("```json")[1].split("```")[0]
            elif "```" in json_str:
                json_str = json_str.split("```")[1].split("```")[0]

            parsed = json.loads(json_str.strip())
            assets = parsed.get("assets", [])
            logger.info(f"추가 에셋 리스트 생성 완료: {len(assets)}개")
            return assets

        except json.JSONDecodeError as e:
            logger.error(f"JSON 파싱 실패: {e}\n응답: {raw_response[:500]}")
            raise Exception(f"Failed to parse additional asset list: {e}")
