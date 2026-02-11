import asyncio
import hashlib
import json
from rate_limiter import RateLimiter
from genai_service import GenAIService
from openai_service import OpenAIService
from llm_service import LLMService
from config_loader import load_config
import logging

# Configure basic logger
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("QueueManager")

# 모델 접두사 기반 프로바이더 추론 규칙
_PREFIX_PROVIDER_MAP = [
    (("gpt", "o1-", "o3-", "o4-"), "openai"),
    (("gemini-", "gemma-"), "gemini"),
]


class QueueManager:
    def __init__(self):
        self.config = load_config()
        self._services: dict[str, LLMService] = {}
        self._limiters: dict[str, RateLimiter] = {}
        self._init_providers()
        self.queue = asyncio.Queue()
        self.running = False
        self.worker_task = None
        self._deterministic = self.config.get('deterministic', False)
        self._cache: dict[str, str] = {}
        if self._deterministic:
            logger.info("Deterministic mode enabled: responses will be cached")

    # 프로바이더 이름 → LLMService 클래스 매핑 (테스트 패치 호환을 위해 런타임 해결)
    _PROVIDER_CLASS_NAMES: dict[str, str] = {
        "gemini": "GenAIService",
        "openai": "OpenAIService",
    }

    def _init_providers(self):
        """config의 providers 루프를 돌며 서비스/리미터 생성."""
        import queue_manager as _mod
        providers = self.config.get('providers', {})
        for pname, pconf in providers.items():
            attr = self._PROVIDER_CLASS_NAMES.get(pname)
            if attr is None or not hasattr(_mod, attr):
                logger.warning(f"Unknown provider '{pname}', skipping")
                continue
            cls = getattr(_mod, attr)
            default_model = pconf['models'][0] if pconf['models'] else None
            self._services[pname] = cls(
                api_key=pconf['api_key'],
                default_model=default_model,
            )
            self._limiters[pname] = RateLimiter(pconf['rpm'])
            logger.info(f"Provider '{pname}' initialized: models={pconf['models']}, rpm={pconf['rpm']}")

    def _resolve_provider(self, model: str) -> str:
        """모델명으로 프로바이더를 결정한다."""
        # 1) model_provider_map 조회
        provider_map = self.config.get('model_provider_map', {})
        if model in provider_map:
            return provider_map[model]

        # 2) 접두사 기반 추론
        model_lower = model.lower()
        for prefixes, pname in _PREFIX_PROVIDER_MAP:
            if model_lower.startswith(prefixes):
                return pname

        # 3) 기본: 첫 번째 프로바이더
        if self._services:
            return next(iter(self._services))
        raise ValueError(f"No provider available for model '{model}'")

    @property
    def service(self) -> LLMService:
        """레거시 호환: 첫 번째 프로바이더 서비스 반환."""
        return next(iter(self._services.values()))

    @property
    def limiter(self) -> RateLimiter:
        """레거시 호환: 첫 번째 프로바이더 리미터 반환."""
        return next(iter(self._limiters.values()))

    @property
    def rpm(self) -> int:
        """레거시 호환: 첫 번째 프로바이더 RPM 반환."""
        return self.config.get('rpm', 0)

    async def start(self):
        """Start the background worker."""
        if not self.running:
            self.running = True
            self.worker_task = asyncio.create_task(self._worker())
            logger.info("Queue Manager started.")

    async def stop(self):
        """Stop the background worker."""
        self.running = False
        if self.worker_task:
            self.worker_task.cancel()
            try:
                await self.worker_task
            except asyncio.CancelledError:
                pass
            logger.info("Queue Manager stopped.")

    async def submit_request(self, model: str, messages: list, options: dict = None) -> asyncio.Future:
        """
        Submit a request to the queue. Returns a Future that will await the result.
        """
        loop = asyncio.get_running_loop()
        future = loop.create_future()

        item = {
            'model': model,
            'messages': messages,
            'options': options,
            'future': future
        }

        await self.queue.put(item)
        return future

    async def _worker(self):
        """Background worker to process queue items with rate limiting."""
        while self.running:
            try:
                # Wait for item
                item = await self.queue.get()

                model = item['model']
                msgs = item['messages']
                opts = item['options']
                fut = item['future']

                if fut.cancelled():
                    self.queue.task_done()
                    continue

                # Resolve provider for this model
                provider = self._resolve_provider(model)
                svc = self._services.get(provider)
                lim = self._limiters.get(provider)

                if svc is None:
                    if not fut.cancelled():
                        fut.set_exception(
                            ValueError(f"No service for provider '{provider}' (model={model})")
                        )
                    self.queue.task_done()
                    continue

                # Deterministic 캐시 확인
                if self._deterministic:
                    cache_key = self._make_cache_key(model, msgs)
                    cached = self._cache.get(cache_key)
                    if cached is not None:
                        logger.info(f"Cache hit for model {model} (provider={provider})")
                        if not fut.cancelled():
                            fut.set_result(cached)
                        self.queue.task_done()
                        continue

                # Check Rate Limit
                if lim:
                    await lim.wait_for_slot()

                try:
                    logger.info(f"Processing request for model {model} (provider={provider})...")
                    result = await svc.generate_response(model, msgs, opts)
                    if self._deterministic:
                        self._cache[cache_key] = result
                        logger.debug(f"Cached response for model {model} (key={cache_key[:16]}...)")
                    if not fut.cancelled():
                        fut.set_result(result)
                except Exception as e:
                    logger.error(f"Error processing request: {e}")
                    if not fut.cancelled():
                        fut.set_exception(e)
                finally:
                    self.queue.task_done()

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Worker error: {e}")
                # Prevent worker crash loop
                await asyncio.sleep(1)

    @staticmethod
    def _make_cache_key(model: str, messages: list) -> str:
        """모델 + 메시지 내용으로 캐시 키 생성."""
        raw = json.dumps({"model": model, "messages": messages}, sort_keys=True, ensure_ascii=False)
        return hashlib.sha256(raw.encode("utf-8")).hexdigest()

    def get_status(self):
        """Return status for health check."""
        providers_status = {}
        for pname, lim in self._limiters.items():
            providers_status[pname] = {"rpm": lim.rpm}
        status = {
            "queue_size": self.queue.qsize(),
            "rpm_config": self.rpm,
            "providers": providers_status,
        }
        if self._deterministic:
            status["deterministic_cache_size"] = len(self._cache)
        return status
