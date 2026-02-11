from abc import ABC, abstractmethod


class LLMService(ABC):
    """LLM 프로바이더 공통 인터페이스."""

    @abstractmethod
    async def generate_response(self, model_name: str, messages: list, options: dict = None) -> str:
        """모델에 메시지를 보내고 응답 텍스트를 반환한다."""
        ...

    @abstractmethod
    def get_provider_name(self) -> str:
        """프로바이더 이름을 반환한다 (예: 'gemini', 'openai')."""
        ...
