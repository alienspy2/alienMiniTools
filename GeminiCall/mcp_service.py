"""
MCP 서버 관리 및 프롬프트 기반 도구 호출 서비스.

McpServerRegistry: MCP 서버 목록 관리 (메모리 + mcp.json 영속화)
McpToolService: 도구 수집/호출/파싱 + ReAct 에이전트 루프
"""

import asyncio
import json
import logging
import os
import re
from typing import Optional

from mcp import ClientSession
from mcp.client.sse import sse_client

logger = logging.getLogger("McpService")


# ── 프롬프트 템플릿 ──────────────────────────────────────────────

SYSTEM_PROMPT_TEMPLATE = """\
You are a helpful assistant with access to the following tools:

{tools_description}

When the user asks you to do something that requires a tool, respond with ONLY a JSON object in this exact format (no other text):
```json
{{"tool": "<tool_name>", "arguments": {{<key>: <value>, ...}}}}
```

If no tool is needed, respond normally in plain text.
"""


# ── MCP 서버 레지스트리 ──────────────────────────────────────────

class McpServerRegistry:
    """등록된 MCP 서버 목록을 메모리 + mcp.json으로 관리합니다."""

    def __init__(self, config_dir: str = None):
        self._servers: dict[str, dict] = {}
        if config_dir is None:
            config_dir = os.path.dirname(os.path.abspath(__file__))
        self._json_path = os.path.join(config_dir, "mcp.json")
        self._load()

    def _load(self):
        """mcp.json에서 서버 목록 로드. 파일 없으면 빈 배열로 생성."""
        if not os.path.exists(self._json_path):
            self._save()
            return
        try:
            with open(self._json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            for entry in data:
                self._servers[entry["name"]] = {"name": entry["name"], "url": entry["url"]}
            logger.info(f"Loaded {len(self._servers)} MCP servers from {self._json_path}")
        except Exception as e:
            logger.error(f"Failed to load mcp.json: {e}")

    def _save(self):
        """현재 서버 목록을 mcp.json에 저장."""
        try:
            data = [{"name": v["name"], "url": v["url"]} for v in self._servers.values()]
            with open(self._json_path, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
        except Exception as e:
            logger.error(f"Failed to save mcp.json: {e}")

    def add(self, name: str, url: str) -> dict:
        entry = {"name": name, "url": url}
        self._servers[name] = entry
        self._save()
        return entry

    def remove(self, name: str) -> bool:
        if name in self._servers:
            del self._servers[name]
            self._save()
            return True
        return False

    def list_all(self) -> list[dict]:
        return list(self._servers.values())

    def get(self, name: str) -> Optional[dict]:
        return self._servers.get(name)

    def get_all_urls(self) -> dict[str, str]:
        return {name: info["url"] for name, info in self._servers.items()}


# ── MCP 도구 서비스 ──────────────────────────────────────────────

class McpToolService:
    """MCP 도구 수집, 호출, JSON 파싱 및 ReAct 에이전트 루프."""

    # ── 도구 수집 ──

    async def collect_tools_from_servers(
        self, server_urls: dict[str, str], timeout: float = 30.0
    ) -> dict[str, list]:
        """여러 MCP 서버에 병렬 SSE 연결하여 도구 목록 수집."""
        results: dict[str, list] = {}

        async def _fetch(name: str, url: str):
            try:
                async with sse_client(url=url) as (read, write):
                    async with ClientSession(read, write) as session:
                        await session.initialize()
                        tools_result = await session.list_tools()
                        results[name] = tools_result.tools
                        logger.info(f"MCP '{name}': {len(tools_result.tools)} tools collected")
            except Exception as e:
                logger.error(f"MCP '{name}' ({url}) tool collection failed: {e}")
                results[name] = []

        tasks = [
            asyncio.wait_for(_fetch(name, url), timeout=timeout)
            for name, url in server_urls.items()
        ]
        await asyncio.gather(*tasks, return_exceptions=True)
        return results

    # ── 도구 호출 ──

    async def call_mcp_tool(
        self, server_url: str, tool_name: str, arguments: dict, timeout: float = 30.0
    ) -> str:
        """MCP 서버에 SSE 연결하여 도구 호출. 타임아웃 적용."""
        async def _call():
            async with sse_client(url=server_url) as (read, write):
                async with ClientSession(read, write) as session:
                    await session.initialize()
                    result = await session.call_tool(tool_name, arguments=arguments)
                    result_text = ""
                    if result.content:
                        result_text = " ".join(
                            c.text for c in result.content if hasattr(c, "text")
                        )
                    if result.isError:
                        result_text = f"[ERROR] {result_text}"
                    return result_text

        return await asyncio.wait_for(_call(), timeout=timeout)

    # ── 프롬프트 구성 ──

    @staticmethod
    def build_tools_description(tools_by_server: dict[str, list]) -> str:
        """도구 목록 → 프롬프트 텍스트. 이름은 서버명.도구명 형태."""
        lines = []
        for server_name, tools in tools_by_server.items():
            for tool in tools:
                qualified_name = f"{server_name}.{tool.name}"
                params = {}
                if tool.inputSchema and tool.inputSchema.get("properties"):
                    required = tool.inputSchema.get("required", [])
                    for pname, pinfo in tool.inputSchema["properties"].items():
                        params[pname] = {
                            "type": pinfo.get("type", "string"),
                            "description": pinfo.get("description", ""),
                            "required": pname in required,
                        }
                lines.append(json.dumps({
                    "name": qualified_name,
                    "description": tool.description or "",
                    "parameters": params,
                }, ensure_ascii=False))
        return "\n".join(lines)

    @staticmethod
    def build_system_prompt(tools_description: str) -> str:
        return SYSTEM_PROMPT_TEMPLATE.format(tools_description=tools_description)

    # ── JSON 파싱 + Format Fixer ──

    @staticmethod
    def extract_tool_call(text: str) -> Optional[dict]:
        """모델 응답에서 tool call JSON 추출. 불완전 JSON 보정 포함."""
        # 1차: ```json ... ``` 코드블록
        m = re.search(r"```(?:json)?\s*(\{.*?\})\s*```", text, re.DOTALL)
        if m:
            obj = McpToolService._try_parse_json(m.group(1))
            if obj and "tool" in obj:
                return obj

        # 2차: bare JSON (nested braces 지원)
        m = re.search(r'\{[^{}]*"tool"\s*:\s*"[^"]*"[^{}]*(?:\{[^{}]*\}[^{}]*)?\}', text, re.DOTALL)
        if m:
            obj = McpToolService._try_parse_json(m.group(0))
            if obj and "tool" in obj:
                return obj

        # 3차: format fixer - 작은따옴표, trailing comma 등 보정
        # 가장 바깥 { ... } 추출 (greedy)
        m = re.search(r"\{.*\}", text, re.DOTALL)
        if m:
            fixed = McpToolService._fix_json(m.group(0))
            obj = McpToolService._try_parse_json(fixed)
            if obj and "tool" in obj:
                return obj

        return None

    @staticmethod
    def _try_parse_json(text: str) -> Optional[dict]:
        try:
            return json.loads(text)
        except (json.JSONDecodeError, ValueError):
            return None

    @staticmethod
    def _fix_json(text: str) -> str:
        """불완전 JSON 보정: 작은따옴표→큰따옴표, trailing comma 제거."""
        text = text.replace("'", '"')
        text = re.sub(r",\s*}", "}", text)
        text = re.sub(r",\s*]", "]", text)
        return text

    # ── qualified name 파싱 ──

    @staticmethod
    def parse_qualified_tool_name(qualified_name: str) -> tuple[Optional[str], str]:
        """'서버명.도구명' → (server_name, tool_name). 점 없으면 (None, name)."""
        if "." in qualified_name:
            parts = qualified_name.split(".", 1)
            return parts[0], parts[1]
        return None, qualified_name

    @staticmethod
    def resolve_server_url(
        server_name: Optional[str],
        tool_name: str,
        server_urls: dict[str, str],
        tools_by_server: dict[str, list],
    ) -> Optional[str]:
        """도구 이름에서 MCP 서버 URL 찾기."""
        if server_name and server_name in server_urls:
            return server_urls[server_name]
        # 서버 이름 없으면 도구를 가진 첫 번째 서버 반환
        for srv_name, tools in tools_by_server.items():
            for tool in tools:
                if tool.name == tool_name:
                    return server_urls.get(srv_name)
        return None

    # ── ReAct 에이전트 루프 ──

    async def run_agent_loop(
        self,
        qm,  # QueueManager
        server_urls: dict[str, str],
        model: str,
        messages: list[dict],
        options: dict,
        max_iterations: int = 5,
        tool_timeout: float = 30.0,
    ) -> str:
        """
        ReAct 에이전트 루프:
        1. MCP 도구 목록 수집 → system prompt 구성
        2. 루프: LLM 호출 → tool call 파싱 → MCP 호출 → 대화 내역 누적 → 반복
        3. tool call 없으면 최종 답변 반환
        """
        logger.debug(f"[AgentLoop] start: model={model}, servers={list(server_urls.keys())}, "
                     f"max_iterations={max_iterations}, tool_timeout={tool_timeout}")

        # 도구 수집
        tools_by_server = await self.collect_tools_from_servers(server_urls, timeout=tool_timeout)
        total_tools = sum(len(t) for t in tools_by_server.values())
        if total_tools == 0:
            logger.warning("No tools collected from MCP servers")

        if logger.isEnabledFor(logging.DEBUG):
            for srv_name, tools in tools_by_server.items():
                tool_names = [t.name for t in tools]
                logger.debug(f"[AgentLoop] server '{srv_name}': {len(tools)} tools -> {tool_names}")

        # system prompt 구성
        tools_desc = self.build_tools_description(tools_by_server)
        system_prompt = self.build_system_prompt(tools_desc)
        logger.debug(f"[AgentLoop] system prompt length={len(system_prompt)}")

        # 대화 내역: system + 사용자 메시지
        conversation = [{"role": "system", "content": system_prompt}] + list(messages)
        logger.debug(f"[AgentLoop] initial conversation: {len(conversation)} messages")

        last_response = ""
        for round_idx in range(max_iterations):
            logger.debug(f"[AgentLoop] === round {round_idx + 1}/{max_iterations} ===")
            logger.debug(f"[AgentLoop] conversation length: {len(conversation)} messages, "
                         f"~{sum(len(m['content']) for m in conversation)} chars")

            # LLM 호출 (QueueManager 경유 → RPM 제한 적용)
            future = await qm.submit_request(model, conversation, options)
            llm_response = await future
            last_response = llm_response

            logger.info(f"Agent round {round_idx + 1}: {llm_response[:100]}...")
            logger.debug(f"[AgentLoop] LLM full response ({len(llm_response)} chars):\n{llm_response}")

            # tool call 파싱
            tool_call = self.extract_tool_call(llm_response)
            if not tool_call:
                logger.debug(f"[AgentLoop] no tool call detected -> final answer")
                return llm_response

            qualified_name = tool_call["tool"]
            tool_args = tool_call.get("arguments", {})
            server_name, tool_name = self.parse_qualified_tool_name(qualified_name)

            logger.info(f"Tool call: {qualified_name}({json.dumps(tool_args, ensure_ascii=False)})")
            logger.debug(f"[AgentLoop] parsed: server={server_name}, tool={tool_name}, args={tool_args}")

            # MCP 서버 URL 찾기
            tool_server_url = self.resolve_server_url(
                server_name, tool_name, server_urls, tools_by_server
            )
            if not tool_server_url:
                error_msg = f"MCP server for tool '{qualified_name}' not found"
                logger.error(error_msg)
                # 에러를 observation으로 추가하고 계속
                conversation.append({"role": "assistant", "content": llm_response})
                conversation.append({"role": "user", "content": f"[Tool Error] {error_msg}"})
                continue

            logger.debug(f"[AgentLoop] calling MCP: {tool_server_url} -> {tool_name}")

            # MCP 도구 호출
            try:
                tool_result = await self.call_mcp_tool(
                    tool_server_url, tool_name, tool_args, timeout=tool_timeout
                )
            except asyncio.TimeoutError:
                tool_result = f"[ERROR] Tool '{qualified_name}' timed out after {tool_timeout}s"
                logger.error(tool_result)
            except Exception as e:
                tool_result = f"[ERROR] Tool '{qualified_name}' failed: {e}"
                logger.error(tool_result)

            logger.info(f"Tool result: {tool_result[:200]}...")
            logger.debug(f"[AgentLoop] tool result full ({len(tool_result)} chars):\n{tool_result}")

            # 대화 내역에 assistant 응답 + observation 추가
            conversation.append({"role": "assistant", "content": llm_response})
            conversation.append({
                "role": "user",
                "content": f"[Tool Result: {qualified_name}]\n{tool_result}"
            })

        logger.warning(f"Max iterations ({max_iterations}) reached")
        return last_response
