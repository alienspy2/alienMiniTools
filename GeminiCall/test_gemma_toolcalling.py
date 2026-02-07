"""
Gemma 3 + MCP Tool Calling 테스트 (프롬프트 기반)

Gemma 3는 네이티브 function calling을 지원하지 않으므로,
프롬프트로 JSON tool call을 생성하게 하고 → MCP 직접 호출 → 결과를 다시 Gemma에게 전달합니다.

흐름:
  1) 도구 목록 + 사용자 질문 → Gemma 3에게 tool call JSON 요청
  2) 응답에서 JSON 파싱 → MCP session.call_tool() 직접 호출
  3) MCP 결과 → Gemma 3에게 최종 답변 요청

사용법:
  uv run python test_gemma_toolcalling.py
  uv run python test_gemma_toolcalling.py --model gemma-3-27b-it
  uv run python test_gemma_toolcalling.py --mcp-url http://localhost:8080/sse
  uv run python test_gemma_toolcalling.py --prompt "서버 상태를 확인해줘"
"""

import argparse
import asyncio
import json
import os
import re

from google import genai
from mcp import ClientSession
from mcp.client.sse import sse_client


# ── 설정 ──────────────────────────────────────────────────────────

DEFAULT_MCP_URL = "http://192.168.0.18:23016/sse"
DEFAULT_MODEL = "gemma-3-4b-it"
DEFAULT_PROMPT = "generate_speech 도구를 사용해서 '안녕하세요'를 음성으로 생성해줘. language는 Korean, speaker는 Sohee로 설정해."
MAX_TOOL_ROUNDS = 10


def load_api_key() -> str:
    script_dir = os.path.dirname(os.path.abspath(__file__))
    config_path = os.path.join(script_dir, "config.json")
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            config = json.load(f)
            api_key = config.get("api_key")
            if not api_key:
                raise ValueError("API Key not found in config.json")
            return api_key
    except FileNotFoundError:
        print(f"Error: config.json not found at {config_path}")
        exit(1)
    except Exception as e:
        print(f"Error loading config.json: {e}")
        exit(1)


# ── MCP 도구 목록 → 프롬프트용 텍스트 ────────────────────────────

def build_tools_description(mcp_tools) -> str:
    """MCP 도구 목록을 시스템 프롬프트용 텍스트로 변환합니다."""
    lines = []
    for tool in mcp_tools.tools:
        params = {}
        required = []
        if tool.inputSchema and tool.inputSchema.get("properties"):
            required = tool.inputSchema.get("required", [])
            for pname, pinfo in tool.inputSchema["properties"].items():
                params[pname] = {
                    "type": pinfo.get("type", "string"),
                    "description": pinfo.get("description", ""),
                    "required": pname in required,
                }
        lines.append(json.dumps({
            "name": tool.name,
            "description": tool.description or "",
            "parameters": params,
        }, ensure_ascii=False))
    return "\n".join(lines)


SYSTEM_PROMPT_TEMPLATE = """\
You are a helpful assistant with access to the following tools:

{tools_description}

When the user asks you to do something that requires a tool, respond with ONLY a JSON object in this exact format (no other text):
```json
{{"tool": "<tool_name>", "arguments": {{<key>: <value>, ...}}}}
```

If no tool is needed, respond normally in plain text.
"""

FOLLOWUP_PROMPT_TEMPLATE = """\
You called the tool `{tool_name}` and got this result:

{tool_result}

Based on this result, provide a helpful response to the user's original request: "{original_prompt}"
"""


# ── JSON 파싱 ────────────────────────────────────────────────────

def extract_tool_call(text: str) -> dict | None:
    """모델 응답에서 tool call JSON을 추출합니다."""
    # ```json ... ``` 블록에서 추출 시도
    m = re.search(r"```(?:json)?\s*(\{.*?\})\s*```", text, re.DOTALL)
    if m:
        try:
            obj = json.loads(m.group(1))
            if "tool" in obj:
                return obj
        except json.JSONDecodeError:
            pass

    # 코드블록 없이 바로 JSON인 경우
    m = re.search(r"\{[^{}]*\"tool\"[^{}]*\}", text, re.DOTALL)
    if m:
        try:
            obj = json.loads(m.group(0))
            if "tool" in obj:
                return obj
        except json.JSONDecodeError:
            pass

    return None


# ── 에이전트 루프 ────────────────────────────────────────────────

async def run_agent(
    mcp_url: str,
    model_id: str,
    prompt: str,
):
    api_key = load_api_key()
    client = genai.Client(api_key=api_key)

    print(f"[*] MCP 서버 연결: {mcp_url}")
    async with sse_client(url=mcp_url) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            print("[*] MCP 세션 초기화 완료")

            # 도구 목록 조회
            mcp_tools = await session.list_tools()
            print(f"\n[*] 사용 가능한 MCP 도구 ({len(mcp_tools.tools)}개):")
            print("-" * 50)
            for i, tool in enumerate(mcp_tools.tools, 1):
                print(f"  {i}. {tool.name}")
                if tool.description:
                    print(f"     설명: {tool.description}")
                if tool.inputSchema and tool.inputSchema.get("properties"):
                    props = tool.inputSchema["properties"]
                    required = tool.inputSchema.get("required", [])
                    print(f"     파라미터:")
                    for pname, pinfo in props.items():
                        req_mark = " (필수)" if pname in required else ""
                        ptype = pinfo.get("type", "any")
                        pdesc = pinfo.get("description", "")
                        desc_str = f" - {pdesc}" if pdesc else ""
                        print(f"       • {pname}: {ptype}{req_mark}{desc_str}")
                else:
                    print(f"     파라미터: 없음")
            print("-" * 50)

            # ── 1단계: Gemma에게 tool call JSON 요청 ──
            tools_desc = build_tools_description(mcp_tools)
            system_prompt = SYSTEM_PROMPT_TEMPLATE.format(tools_description=tools_desc)

            print(f"\n[USER] {prompt}\n")

            response = client.models.generate_content(
                model=model_id,
                contents=f"{system_prompt}\n\nUser: {prompt}",
            )
            gemma_text = response.text
            print(f"[GEMMA 응답] {gemma_text}")

            # ── 2단계: JSON 파싱 → MCP 호출 ──
            tool_call = extract_tool_call(gemma_text)
            if not tool_call:
                print("\n[*] 도구 호출 없이 직접 응답했습니다.")
                return gemma_text

            tool_name = tool_call["tool"]
            tool_args = tool_call.get("arguments", {})
            print(f"\n[TOOL CALL] {tool_name}({json.dumps(tool_args, ensure_ascii=False)})")

            # MCP 도구 호출
            result = await session.call_tool(tool_name, arguments=tool_args)

            result_text = ""
            if result.content:
                result_text = " ".join(
                    c.text for c in result.content if hasattr(c, "text")
                )
            if result.isError:
                result_text = f"[ERROR] {result_text}"

            print(f"[TOOL RESULT] {result_text[:500]}{'...' if len(result_text) > 500 else ''}")

            # ── 3단계: 결과를 Gemma에게 전달하여 최종 답변 ──
            followup = FOLLOWUP_PROMPT_TEMPLATE.format(
                tool_name=tool_name,
                tool_result=result_text,
                original_prompt=prompt,
            )

            response2 = client.models.generate_content(
                model=model_id,
                contents=followup,
            )
            final_text = response2.text
            print(f"\n[ASSISTANT] {final_text}")
            return final_text


# ── 메인 ─────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Gemma 3 + MCP Tool Calling 테스트 (프롬프트 기반)")
    parser.add_argument("--mcp-url", default=DEFAULT_MCP_URL, help="MCP 서버 SSE URL")
    parser.add_argument("--model", default=DEFAULT_MODEL, help="사용할 모델 ID")
    parser.add_argument("--prompt", default=DEFAULT_PROMPT, help="질문 프롬프트")
    args = parser.parse_args()

    print("=" * 60)
    print("Gemma 3 + MCP Tool Calling 테스트 (프롬프트 기반)")
    print(f"  모델: {args.model}")
    print(f"  MCP:  {args.mcp_url}")
    print("=" * 60)

    asyncio.run(run_agent(args.mcp_url, args.model, args.prompt))


if __name__ == "__main__":
    main()
