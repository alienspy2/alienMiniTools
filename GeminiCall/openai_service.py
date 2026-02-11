from openai import OpenAI
import asyncio
import logging
from llm_service import LLMService

logger = logging.getLogger("OpenAIService")


class OpenAIService(LLMService):
    def __init__(self, api_key: str, default_model: str = None):
        self.client = OpenAI(api_key=api_key)
        self.default_model = default_model

    def get_provider_name(self) -> str:
        return "openai"

    @staticmethod
    def _convert_messages(ollama_messages: list) -> list[dict]:
        """Ollama 포맷 메시지를 OpenAI ChatCompletion 포맷으로 변환.

        Ollama와 OpenAI 포맷이 거의 동일하므로 role만 정리한다.
        """
        converted = []
        for msg in ollama_messages:
            role = msg.get("role", "user")
            content = msg.get("content", "")
            converted.append({"role": role, "content": content})
        return converted

    async def generate_response(self, model_name: str, messages: list, options: dict = None) -> str:
        target_model = model_name if model_name else self.default_model

        openai_messages = self._convert_messages(messages)

        kwargs: dict = {
            "model": target_model,
            "messages": openai_messages,
        }

        if options:
            if "temperature" in options:
                kwargs["temperature"] = options["temperature"]
            if "top_p" in options:
                kwargs["top_p"] = options["top_p"]
            if "reasoning_effort" in options:
                kwargs["reasoning_effort"] = options["reasoning_effort"]
            max_tokens = options.get("max_output_tokens") or options.get("num_predict")
            if max_tokens:
                kwargs["max_tokens"] = max_tokens

        loop = asyncio.get_running_loop()

        def _call_api():
            return self.client.chat.completions.create(**kwargs)

        response = await loop.run_in_executor(None, _call_api)

        return response.choices[0].message.content
