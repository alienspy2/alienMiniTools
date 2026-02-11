"""MCP 엔드포인트 통합 테스트."""

import pytest
from fastapi.testclient import TestClient
from unittest.mock import patch, AsyncMock

from main import app


@pytest.fixture
def client():
    with patch('queue_manager.GenAIService') as MockService, \
         patch('queue_manager.OpenAIService') as MockOpenAI, \
         patch('queue_manager.RateLimiter') as MockLimiter, \
         patch('main.load_config') as MockConfig, \
         patch('queue_manager.load_config') as MockConfigQM, \
         patch('main.McpServerRegistry') as MockRegistry, \
         patch('main.McpToolService') as MockMcpSvc:

        conf = {
            "rpm": 60, "models": ["gemma-27b"], "http_port": 1234, "api_key": "x",
            "providers": {
                "gemini": {"api_key": "x", "models": ["gemma-27b"], "rpm": 60}
            },
            "all_models": ["gemma-27b"],
            "model_provider_map": {"gemma-27b": "gemini"},
        }
        MockConfig.return_value = conf
        MockConfigQM.return_value = conf

        svc_inst = MockService.return_value
        svc_inst.generate_response = AsyncMock(return_value="AI Response")

        limit_inst = MockLimiter.return_value
        limit_inst.wait_for_slot = AsyncMock()
        limit_inst.rpm = 60

        # McpServerRegistry mock
        registry_inst = MockRegistry.return_value
        registry_inst.list_all.return_value = []
        registry_inst.add.side_effect = lambda name, url: {"name": name, "url": url}
        _servers = {}

        def mock_add(name, url):
            _servers[name] = {"name": name, "url": url}
            registry_inst.list_all.return_value = list(_servers.values())
            registry_inst.get_all_urls.return_value = {n: v["url"] for n, v in _servers.items()}
            return {"name": name, "url": url}

        def mock_remove(name):
            if name in _servers:
                del _servers[name]
                registry_inst.list_all.return_value = list(_servers.values())
                registry_inst.get_all_urls.return_value = {n: v["url"] for n, v in _servers.items()}
                return True
            return False

        def mock_get(name):
            return _servers.get(name)

        registry_inst.add.side_effect = mock_add
        registry_inst.remove.side_effect = mock_remove
        registry_inst.get.side_effect = mock_get
        registry_inst.get_all_urls.return_value = {}

        # McpToolService mock
        mcp_svc_inst = MockMcpSvc.return_value
        mcp_svc_inst.run_agent_loop = AsyncMock(return_value="MCP Agent Response")

        with TestClient(app) as c:
            yield c


def test_mcp_add_server(client):
    resp = client.post("/mcp/add", json={"name": "tts", "url": "http://localhost:8080/sse"})
    assert resp.status_code == 200
    data = resp.json()
    assert data["name"] == "tts"
    assert data["url"] == "http://localhost:8080/sse"
    assert "registered" in data["message"]


def test_mcp_list_servers(client):
    client.post("/mcp/add", json={"name": "srv1", "url": "http://srv1/sse"})
    client.post("/mcp/add", json={"name": "srv2", "url": "http://srv2/sse"})
    resp = client.get("/mcp/list")
    assert resp.status_code == 200
    data = resp.json()
    assert len(data["servers"]) == 2


def test_mcp_remove_server(client):
    client.post("/mcp/add", json={"name": "to-remove", "url": "http://x/sse"})
    resp = client.request("DELETE", "/mcp/remove", json={"name": "to-remove"})
    assert resp.status_code == 200

    # 다시 삭제하면 404
    resp = client.request("DELETE", "/mcp/remove", json={"name": "to-remove"})
    assert resp.status_code == 404


def test_mcp_remove_nonexistent(client):
    resp = client.request("DELETE", "/mcp/remove", json={"name": "ghost"})
    assert resp.status_code == 404


def test_generate_with_mcp_no_servers(client):
    """MCP 서버가 없으면 일반 generate로 폴백."""
    payload = {
        "jsonrpc": "2.0",
        "method": "generate_content",
        "params": {
            "messages": [{"role": "user", "content": "hello"}]
        },
        "id": 1
    }
    resp = client.post("/generate_with_mcp", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["result"]["generated_text"] == "AI Response"


def test_generate_with_mcp_invalid_jsonrpc(client):
    payload = {
        "jsonrpc": "1.0",
        "method": "generate_content",
        "params": {"messages": [{"role": "user", "content": "hi"}]},
        "id": 1
    }
    resp = client.post("/generate_with_mcp", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["error"]["code"] == -32600


def test_generate_with_mcp_invalid_method(client):
    payload = {
        "jsonrpc": "2.0",
        "method": "unknown_method",
        "params": {"messages": [{"role": "user", "content": "hi"}]},
        "id": 1
    }
    resp = client.post("/generate_with_mcp", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["error"]["code"] == -32601


def test_generate_with_mcp_with_servers(client):
    """MCP 서버가 있으면 에이전트 루프 호출."""
    client.post("/mcp/add", json={"name": "tts", "url": "http://tts/sse"})
    payload = {
        "jsonrpc": "2.0",
        "method": "generate_content",
        "params": {
            "messages": [{"role": "user", "content": "hello"}],
            "max_iterations": 3
        },
        "id": 2
    }
    resp = client.post("/generate_with_mcp", json=payload)
    assert resp.status_code == 200
    data = resp.json()
    assert data["result"]["generated_text"] == "MCP Agent Response"
