"""McpServerRegistry 및 McpToolService 단위 테스트."""

import json
import os
import tempfile
import pytest
from unittest.mock import MagicMock

from mcp_service import McpServerRegistry, McpToolService


# --- McpServerRegistry ---

class TestMcpServerRegistry:
    def _make_registry(self, tmp_path):
        return McpServerRegistry(config_dir=str(tmp_path))

    def test_init_creates_empty_mcp_json(self, tmp_path):
        registry = self._make_registry(tmp_path)
        mcp_json = tmp_path / "mcp.json"
        assert mcp_json.exists()
        assert json.loads(mcp_json.read_text()) == []

    def test_add_and_list(self, tmp_path):
        registry = self._make_registry(tmp_path)
        registry.add("tts", "http://localhost:8080/sse")
        result = registry.list_all()
        assert len(result) == 1
        assert result[0]["name"] == "tts"
        assert result[0]["url"] == "http://localhost:8080/sse"

    def test_add_overwrites(self, tmp_path):
        registry = self._make_registry(tmp_path)
        registry.add("tts", "http://old/sse")
        registry.add("tts", "http://new/sse")
        assert len(registry.list_all()) == 1
        assert registry.get("tts")["url"] == "http://new/sse"

    def test_remove(self, tmp_path):
        registry = self._make_registry(tmp_path)
        registry.add("tts", "http://localhost/sse")
        assert registry.remove("tts") is True
        assert registry.remove("tts") is False
        assert len(registry.list_all()) == 0

    def test_get(self, tmp_path):
        registry = self._make_registry(tmp_path)
        registry.add("tts", "http://localhost/sse")
        assert registry.get("tts")["url"] == "http://localhost/sse"
        assert registry.get("nonexistent") is None

    def test_get_all_urls(self, tmp_path):
        registry = self._make_registry(tmp_path)
        registry.add("a", "http://a/sse")
        registry.add("b", "http://b/sse")
        urls = registry.get_all_urls()
        assert urls == {"a": "http://a/sse", "b": "http://b/sse"}

    def test_persistence(self, tmp_path):
        """add/remove 후 새 인스턴스에서 복원 확인."""
        registry = self._make_registry(tmp_path)
        registry.add("srv1", "http://srv1/sse")
        registry.add("srv2", "http://srv2/sse")
        registry.remove("srv1")

        # 새 인스턴스 생성 → mcp.json에서 로드
        registry2 = self._make_registry(tmp_path)
        assert len(registry2.list_all()) == 1
        assert registry2.get("srv2")["url"] == "http://srv2/sse"


# --- McpToolService: extract_tool_call ---

class TestExtractToolCall:
    svc = McpToolService()

    def test_codeblock_json(self):
        text = '```json\n{"tool": "my_tool", "arguments": {"key": "value"}}\n```'
        result = self.svc.extract_tool_call(text)
        assert result is not None
        assert result["tool"] == "my_tool"
        assert result["arguments"]["key"] == "value"

    def test_bare_json(self):
        text = '{"tool": "my_tool", "arguments": {}}'
        result = self.svc.extract_tool_call(text)
        assert result is not None
        assert result["tool"] == "my_tool"

    def test_json_in_text(self):
        text = 'Here is the tool call: {"tool": "test", "arguments": {"a": 1}} done.'
        result = self.svc.extract_tool_call(text)
        assert result is not None
        assert result["tool"] == "test"

    def test_no_match(self):
        text = "그냥 일반 텍스트 응답입니다."
        assert self.svc.extract_tool_call(text) is None

    def test_format_fixer_single_quotes(self):
        text = "{'tool': 'my_tool', 'arguments': {'key': 'val'}}"
        result = self.svc.extract_tool_call(text)
        assert result is not None
        assert result["tool"] == "my_tool"

    def test_format_fixer_trailing_comma(self):
        text = '{"tool": "my_tool", "arguments": {"key": "val",},}'
        result = self.svc.extract_tool_call(text)
        assert result is not None
        assert result["tool"] == "my_tool"


# --- McpToolService: parse_qualified_tool_name ---

class TestParseQualifiedToolName:
    svc = McpToolService()

    def test_with_server(self):
        server, tool = self.svc.parse_qualified_tool_name("server1.my_tool")
        assert server == "server1"
        assert tool == "my_tool"

    def test_without_server(self):
        server, tool = self.svc.parse_qualified_tool_name("my_tool")
        assert server is None
        assert tool == "my_tool"

    def test_multiple_dots(self):
        server, tool = self.svc.parse_qualified_tool_name("ns.sub.tool")
        assert server == "ns"
        assert tool == "sub.tool"


# --- McpToolService: build_tools_description ---

class TestBuildToolsDescription:
    svc = McpToolService()

    def test_single_tool(self):
        mock_tool = MagicMock()
        mock_tool.name = "test_tool"
        mock_tool.description = "A test tool"
        mock_tool.inputSchema = {
            "properties": {
                "param1": {"type": "string", "description": "First param"}
            },
            "required": ["param1"]
        }

        desc = self.svc.build_tools_description({"srv": [mock_tool]})
        parsed = json.loads(desc)
        assert parsed["name"] == "srv.test_tool"
        assert "param1" in parsed["parameters"]
        assert parsed["parameters"]["param1"]["required"] is True

    def test_no_tools(self):
        desc = self.svc.build_tools_description({})
        assert desc == ""

    def test_multiple_servers(self):
        tool_a = MagicMock()
        tool_a.name = "tool_a"
        tool_a.description = "Tool A"
        tool_a.inputSchema = None

        tool_b = MagicMock()
        tool_b.name = "tool_b"
        tool_b.description = "Tool B"
        tool_b.inputSchema = {"properties": {}}

        desc = self.svc.build_tools_description({"s1": [tool_a], "s2": [tool_b]})
        lines = desc.strip().split("\n")
        assert len(lines) == 2


# --- McpToolService: build_system_prompt ---

class TestBuildSystemPrompt:
    svc = McpToolService()

    def test_contains_tools(self):
        prompt = self.svc.build_system_prompt("tool description here")
        assert "tool description here" in prompt
        assert "JSON" in prompt


# --- McpToolService: resolve_server_url ---

class TestResolveServerUrl:
    svc = McpToolService()

    def test_by_server_name(self):
        url = self.svc.resolve_server_url(
            "srv1", "tool", {"srv1": "http://srv1/sse"}, {}
        )
        assert url == "http://srv1/sse"

    def test_by_tool_name_fallback(self):
        mock_tool = MagicMock()
        mock_tool.name = "my_tool"
        url = self.svc.resolve_server_url(
            None, "my_tool",
            {"srv1": "http://srv1/sse"},
            {"srv1": [mock_tool]}
        )
        assert url == "http://srv1/sse"

    def test_not_found(self):
        url = self.svc.resolve_server_url(None, "ghost", {}, {})
        assert url is None
