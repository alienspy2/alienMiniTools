
import asyncio
from rate_limiter import RateLimiter
from genai_service import GenAIService
from config_loader import load_config
import logging

# Configure basic logger
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("QueueManager")

class QueueManager:
    def __init__(self):
        self.config = load_config()
        self.rpm = self.config['rpm']
        self.limiter = RateLimiter(self.rpm)
        self.service = GenAIService()
        self.queue = asyncio.Queue()
        self.running = False
        self.worker_task = None

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
                
                # Check Rate Limit
                await self.limiter.wait_for_slot()
                
                # Process
                model = item['model']
                msgs = item['messages']
                opts = item['options']
                fut = item['future']
                
                if fut.cancelled():
                    self.queue.task_done()
                    continue

                try:
                    logger.info(f"Processing request for model {model}...")
                    result = await self.service.generate_response(model, msgs, opts)
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

    def get_status(self):
        """Return status for health check."""
        return {
            "queue_size": self.queue.qsize(),
            "rpm_config": self.rpm,
            # Rate limiter internal state access could be added here if needed
        }
