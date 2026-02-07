
import pytest
import asyncio
from unittest.mock import MagicMock, AsyncMock, patch
from queue_manager import QueueManager

@pytest.fixture
def mock_dependencies():
    with patch('queue_manager.load_config') as mock_conf, \
         patch('queue_manager.GenAIService') as mock_svc_cls, \
         patch('queue_manager.RateLimiter') as mock_limit_cls:
        
        mock_conf.return_value = {"rpm": 60, "models": [], "api_key": "k", "http_port": 0}
        
        # Setup Service Mock
        svc_inst = mock_svc_cls.return_value
        svc_inst.generate_response = AsyncMock(return_value="Processed")
        
        # Setup Limiter Mock
        limit_inst = mock_limit_cls.return_value
        limit_inst.wait_for_slot = AsyncMock()
        
        yield svc_inst

@pytest.mark.asyncio
async def test_queue_processing(mock_dependencies):
    qm = QueueManager()
    await qm.start()
    
    try:
        # Submit request
        future = await qm.submit_request("model-x", [{"role": "user", "content": "hi"}])
        
        # Wait for result
        result = await asyncio.wait_for(future, timeout=2.0)
        
        assert result == "Processed"
        # Verify service call
        mock_dependencies.generate_response.assert_called_once()
        
    finally:
        await qm.stop()

@pytest.mark.asyncio
async def test_queue_error_propagation(mock_dependencies):
    # Setup service to fail
    mock_dependencies.generate_response.side_effect = ValueError("API Fail")
    
    qm = QueueManager()
    await qm.start()
    
    try:
        future = await qm.submit_request("model-error", [])
        
        with pytest.raises(ValueError, match="API Fail"):
            await asyncio.wait_for(future, timeout=2.0)
            
    finally:
        await qm.stop()
