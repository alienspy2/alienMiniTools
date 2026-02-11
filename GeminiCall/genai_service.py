from google import genai
from google.genai import types
import asyncio
import logging
from llm_service import LLMService

logger = logging.getLogger("GenAIService")

# system_instruction을 지원하지 않는 모델 접두사
_NO_SYSTEM_INSTRUCTION_PREFIXES = ("gemma",)

class GenAIService(LLMService):
    def __init__(self, api_key: str, default_model: str = None):
        self.client = genai.Client(api_key=api_key)
        self.default_model = default_model

    def get_provider_name(self) -> str:
        return "gemini"

    @staticmethod
    def _supports_system_instruction(model_name: str) -> bool:
        """모델이 system_instruction을 지원하는지 확인"""
        return not model_name.lower().startswith(_NO_SYSTEM_INSTRUCTION_PREFIXES)

    def _convert_messages(self, ollama_messages, model_name: str):
        """
        Convert Ollama style messages to Gemini style contents.
        Ollama: [{'role': 'user', 'content': 'hello'}, ...]
        Gemini: role='user'|'model', parts=[types.Part.from_text(text=...)]

        system_instruction을 지원하지 않는 모델(Gemma 등)은
        시스템 메시지를 첫 번째 사용자 메시지에 합침.
        """
        gemini_contents = []
        system_instruction = None

        for msg in ollama_messages:
            role = msg.get('role')
            content = msg.get('content', '')

            if role == 'system':
                system_instruction = content
                continue

            if role == 'assistant':
                role = 'model'

            gemini_contents.append(
                types.Content(
                    role=role,
                    parts=[types.Part.from_text(text=content)]
                )
            )

        # system_instruction 미지원 모델: 첫 user 메시지에 합침
        if system_instruction and not self._supports_system_instruction(model_name):
            logger.info(f"Model {model_name} does not support system_instruction, prepending to first user message")
            for content_item in gemini_contents:
                if content_item.role == 'user':
                    original_text = content_item.parts[0].text
                    content_item.parts[0] = types.Part.from_text(
                        text=f"[System Instructions]\n{system_instruction}\n\n[User Message]\n{original_text}"
                    )
                    system_instruction = None
                    break

        return gemini_contents, system_instruction

    async def generate_response(self, model_name: str, messages: list, options: dict = None):
        """
        Call Gemini API.
        model_name: specific model to use (must be in config list ideally, or just pass through)
        messages: list of dict {'role':..., 'content':...}
        """
        target_model = model_name if model_name else self.default_model

        contents, sys_inst = self._convert_messages(messages, target_model)

        generate_config = types.GenerateContentConfig()
        if options:
            if 'temperature' in options:
                generate_config.temperature = options['temperature']
            if 'top_p' in options:
                generate_config.top_p = options['top_p']
            if 'max_output_tokens' in options:
                generate_config.max_output_tokens = options['max_output_tokens']

        if sys_inst:
            generate_config.system_instruction = sys_inst

        loop = asyncio.get_running_loop()

        def _call_api():
            return self.client.models.generate_content(
                model=target_model,
                contents=contents,
                config=generate_config
            )

        response = await loop.run_in_executor(None, _call_api)

        # Extract text
        return response.text
