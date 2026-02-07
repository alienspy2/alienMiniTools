
import pytest
import asyncio
import time
from rate_limiter import RateLimiter

@pytest.mark.asyncio
async def test_rate_limiter_under_limit():
    # RPM 10 -> 충분함
    limiter = RateLimiter(10)
    start = time.time()
    
    # 5번 연속 호출
    for _ in range(5):
        await limiter.wait_for_slot()
        
    duration = time.time() - start
    assert duration < 1.0  # 거의 즉시 처리되어야 함

@pytest.mark.asyncio
async def test_rate_limiter_over_limit():
    # RPM 2 -> 2개까지만 즉시, 3번째는 대기
    limiter = RateLimiter(2)
    # 테스트를 위해 interval을 1초로 단축하여 검증
    limiter.interval = 1.0 
    
    start = time.time()
    
    await limiter.wait_for_slot() # 1
    await limiter.wait_for_slot() # 2
    
    # 여기까지는 빨라야 함
    mid = time.time()
    assert mid - start < 0.5
    
    await limiter.wait_for_slot() # 3 (여기서 약 1초 대기 발생해야 함)
    end = time.time()
    
    assert end - start >= 1.0
