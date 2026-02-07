import asyncio
import time
from collections import deque

class RateLimiter:
    def __init__(self, rpm: int):
        self.rpm = rpm
        self.interval = 60.0  # 1 minute window
        self.request_timestamps = deque()
        self._lock = asyncio.Lock()

    async def wait_for_slot(self):
        """
        Waits until a slot is available within the RPM limit.
        Thread-safe (asyncio) using Lock.
        """
        if self.rpm <= 0:
            return  # No limit

        async with self._lock:
            while True:
                now = time.time()
                
                # Remove timestamps older than the interval
                while self.request_timestamps and now - self.request_timestamps[0] > self.interval:
                    self.request_timestamps.popleft()
                
                if len(self.request_timestamps) < self.rpm:
                    self.request_timestamps.append(now)
                    return
                
                # If limit reached, wait until the oldest timestamp expires
                oldest = self.request_timestamps[0]
                wait_time = self.interval - (now - oldest)
                
                if wait_time > 0:
                    await asyncio.sleep(wait_time + 0.05) # Small buffer
