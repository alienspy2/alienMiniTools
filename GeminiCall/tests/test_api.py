
import pytest
from fastapi.testclient import TestClient
from unittest.mock import MagicMock, patch, AsyncMock
from main import app

@pytest.fixture
def client():
    # Patch dependencies inside queue_manager and config
    with patch('queue_manager.GenAIService') as MockService, \
         patch('queue_manager.RateLimiter') as MockLimiter, \
         patch('main.load_config') as MockConfig, \
         patch('queue_manager.load_config') as MockConfigQM:
        
        conf = {
            "rpm": 60, "models": ["gemma-27b"], "http_port": 1234, "api_key": "x"
        }
        MockConfig.return_value = conf
        MockConfigQM.return_value = conf
        
        # Setup Service Mock
        svc_inst = MockService.return_value
        svc_inst.generate_response = AsyncMock(return_value="AI Response")
        
        # Setup Limiter Mock
        limit_inst = MockLimiter.return_value
        limit_inst.wait_for_slot = AsyncMock()
        
        with TestClient(app) as c:
            yield c

def test_health(client):
    resp = client.get("/health")
    assert resp.status_code == 200
    assert resp.json()['status'] == "ok"

def test_ollama_tags(client):
    resp = client.get("/api/tags")
    assert resp.status_code == 200

def test_ollama_chat(client):
    payload = {
        "model": "gemma-27b",
        "messages": [{"role": "user", "content": "hi"}]
    }
    resp = client.post("/api/chat", json=payload)
    print(f"DEBUG RESP: {resp.status_code} {resp.text}")
    assert resp.status_code == 200
    data = resp.json()
    assert data['message']['content'] == "AI Response"
    assert data['done'] is True

def test_json_rpc_success(client):
    payload = {
        "jsonrpc": "2.0",
        "method": "generate_content",
        "params": {
            "messages": [{"role": "user", "parts": ["hi"]}]
        },
        "id": 99
    }
    resp = client.post("/generate", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data['result']['generated_text'] == "AI Response"
    assert data['id'] == 99

def test_json_rpc_invalid_method(client):
    payload = {
        "jsonrpc": "2.0",
        "method": "wrong_method",
        "params": {"messages": []},
        "id": 1
    }
    resp = client.post("/generate", json=payload)
    assert resp.json()['error']['code'] == -32601
