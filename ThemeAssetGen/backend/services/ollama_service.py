import json
import logging
import aiohttp

from backend.config import OLLAMA_URL, OLLAMA_MODEL, OLLAMA_TIMEOUT
from backend.utils.prompt_templates import (
    ASSET_LIST_PROMPT, 
    ADDITIONAL_ASSETS_PROMPT, 
    CATEGORY_ASSET_PROMPT,
    ASSET_CATEGORIES
)

logger = logging.getLogger(__name__)


class OllamaService:
    def __init__(self):
        self.base_url = OLLAMA_URL
        self.model = OLLAMA_MODEL
        self.timeout = aiohttp.ClientTimeout(total=OLLAMA_TIMEOUT)

    async def check_health(self) -> bool:
        """Check Ollama server status"""
        try:
            async with aiohttp.ClientSession(timeout=aiohttp.ClientTimeout(total=5)) as session:
                async with session.get(f"{self.base_url}/api/tags") as response:
                    return response.status == 200
        except Exception:
            return False

    def _parse_json_response(self, raw_response: str) -> list[dict]:
        """Parse JSON from Ollama response"""
        try:
            json_str = raw_response
            if "```json" in json_str:
                json_str = json_str.split("```json")[1].split("```")[0]
            elif "```" in json_str:
                json_str = json_str.split("```")[1].split("```")[0]

            parsed = json.loads(json_str.strip())
            assets = parsed.get("assets", [])
            return assets

        except json.JSONDecodeError as e:
            logger.error(f"JSON parse failed: {e}\nResponse: {raw_response[:500]}")
            raise Exception(f"Failed to parse asset list: {e}")

    async def _call_ollama(self, prompt: str) -> str:
        """Call Ollama API and return raw response"""
        payload = {
            "model": self.model,
            "prompt": prompt,
            "stream": False,
        }

        async with aiohttp.ClientSession(timeout=self.timeout) as session:
            async with session.post(
                f"{self.base_url}/api/generate",
                json=payload
            ) as response:
                if response.status != 200:
                    error = await response.text()
                    logger.error(f"Ollama error: {error}")
                    raise Exception(f"Ollama error: {error}")

                data = await response.json()
                return data.get("response", "")

    async def generate_asset_list(self, theme: str) -> list[dict]:
        """Generate asset list from theme (legacy - simple list)"""
        prompt = ASSET_LIST_PROMPT.format(theme=theme)
        logger.info(f"Ollama request: theme={theme}, model={self.model}")
        
        raw_response = await self._call_ollama(prompt)
        assets = self._parse_json_response(raw_response)
        
        logger.info(f"Asset list generated: {len(assets)} items")
        return assets

    async def generate_category_assets(
        self, 
        theme: str, 
        asset_type: str, 
        existing_names: list[str] = None
    ) -> list[dict]:
        """Generate assets for a specific category type"""
        if asset_type not in ASSET_CATEGORIES:
            raise Exception(f"Unknown asset type: {asset_type}")
        
        category_info = ASSET_CATEGORIES[asset_type]
        count = category_info["count"]
        category = category_info["category"]
        description = category_info["description"]
        
        existing_str = ", ".join(existing_names) if existing_names else "None"
        
        prompt = CATEGORY_ASSET_PROMPT.format(
            theme=theme,
            asset_type=asset_type,
            asset_type_description=description,
            count=count,
            category=category,
            existing_assets=existing_str
        )
        
        logger.info(f"Ollama category request: theme={theme}, type={asset_type}, count={count}")
        
        raw_response = await self._call_ollama(prompt)
        assets = self._parse_json_response(raw_response)
        
        # Ensure asset_type is set on all assets
        for asset in assets:
            asset["asset_type"] = asset_type
            if "category" not in asset:
                asset["category"] = category
        
        logger.info(f"Category assets generated: {len(assets)} items for {asset_type}")
        return assets

    async def generate_additional_assets(
        self, 
        theme: str, 
        existing_names: list[str], 
        count: int = 10,
        asset_type: str = None
    ) -> list[dict]:
        """Generate additional assets, optionally for specific category"""
        
        # If asset_type is specified, use category-specific generation
        if asset_type and asset_type in ASSET_CATEGORIES:
            return await self.generate_category_assets(theme, asset_type, existing_names)
        
        # Otherwise use general additional assets
        existing_str = ", ".join(existing_names) if existing_names else "None"
        prompt = ADDITIONAL_ASSETS_PROMPT.format(
            theme=theme,
            existing_assets=existing_str,
            count=count
        )

        logger.info(f"Ollama additional assets: theme={theme}, count={count}, existing={len(existing_names)}")

        raw_response = await self._call_ollama(prompt)
        assets = self._parse_json_response(raw_response)
        
        logger.info(f"Additional assets generated: {len(assets)} items")
        return assets

    async def refine_prompt(self, prompt: str) -> str:
        """Optimize prompt for image generation"""
        instructions = (
            "Enhance this prompt for 3D asset image generation. "
            "The object should be isolated on white background, single object only, "
            "no lighting for 3D reconstruction. Output ONLY the enhanced prompt."
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
                    return prompt

                data = await response.json()
                refined = data.get("response", "").strip()
                return refined if refined else prompt

    @staticmethod
    def get_asset_categories() -> dict:
        """Get all available asset categories with their info"""
        return ASSET_CATEGORIES
