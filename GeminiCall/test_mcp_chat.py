"""
MCP + Generate 테스트용 CLI 채팅 클라이언트.

사용법:
  uv run python test_mcp_chat.py
  uv run python test_mcp_chat.py --port 20006 --model gemma-3-4b-it

명령어:
  /add <name> <url>   - MCP 서버 등록
  /remove <name>      - MCP 서버 제거
  /list               - 등록된 MCP 서버 목록
  /iter <n>           - max_iterations 변경 (기본 5)
  /clear              - 대화 내역 초기화
  /help               - 명령어 도움말
  exit / quit         - 종료
"""

import requests
import json
import os
import sys
import uuid
import argparse


def load_config():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    config_path = os.path.join(script_dir, "config.json")
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        print(f"Error: config.json not found at {config_path}")
        sys.exit(1)
    except Exception as e:
        print(f"Error loading config.json: {e}")
        sys.exit(1)


def print_help():
    print("""
명령어:
  /add <name> <url>   - MCP 서버 등록
  /remove <name>      - MCP 서버 제거
  /list               - 등록된 MCP 서버 목록
  /iter <n>           - max_iterations 변경 (현재 값 표시)
  /clear              - 대화 내역 초기화
  /help               - 이 도움말
  exit / quit         - 종료

일반 텍스트 입력 시 /generate_with_mcp 엔드포인트로 전송됩니다.
""")


def mcp_add(base_url, name, url):
    try:
        resp = requests.post(f"{base_url}/mcp/add", json={"name": name, "url": url})
        if resp.status_code == 200:
            data = resp.json()
            print(f"  [OK] {data['name']} -> {data['url']} ({data.get('message', '')})")
        else:
            print(f"  [Error] {resp.status_code}: {resp.text}")
    except requests.exceptions.ConnectionError:
        print(f"  [Error] 서버에 연결할 수 없습니다: {base_url}")


def mcp_remove(base_url, name):
    try:
        resp = requests.request("DELETE", f"{base_url}/mcp/remove", json={"name": name})
        if resp.status_code == 200:
            data = resp.json()
            print(f"  [OK] {data['name']} 제거됨")
        elif resp.status_code == 404:
            print(f"  [Not Found] '{name}' 서버가 없습니다")
        else:
            print(f"  [Error] {resp.status_code}: {resp.text}")
    except requests.exceptions.ConnectionError:
        print(f"  [Error] 서버에 연결할 수 없습니다: {base_url}")


def mcp_list(base_url):
    try:
        resp = requests.get(f"{base_url}/mcp/list")
        if resp.status_code == 200:
            data = resp.json()
            servers = data.get("servers", [])
            if not servers:
                print("  등록된 MCP 서버가 없습니다.")
            else:
                print(f"  등록된 MCP 서버 ({len(servers)}개):")
                for s in servers:
                    print(f"    - {s['name']}: {s['url']}")
        else:
            print(f"  [Error] {resp.status_code}: {resp.text}")
    except requests.exceptions.ConnectionError:
        print(f"  [Error] 서버에 연결할 수 없습니다: {base_url}")


def main():
    parser = argparse.ArgumentParser(description="MCP + Generate CLI 채팅 클라이언트")
    parser.add_argument("--port", type=int, default=None, help="서버 포트 (기본: config.json)")
    parser.add_argument("--model", type=str, default="gemma-3-4b-it", help="모델 이름")
    parser.add_argument("--host", type=str, default="localhost", help="서버 호스트")
    parser.add_argument("--max-iter", type=int, default=5, help="max_iterations (기본 5)")
    args = parser.parse_args()

    config = load_config()
    port = args.port or config.get("http_port", 20006)
    model = args.model
    max_iterations = args.max_iter

    base_url = f"http://{args.host}:{port}"
    generate_url = f"{base_url}/generate_with_mcp"

    print(f"--- MCP Chat Client ---")
    print(f"서버: {base_url}")
    print(f"모델: {model}")
    print(f"max_iterations: {max_iterations}")
    print(f"/help 로 명령어 확인. 'exit'로 종료.\n")

    message_history = []

    while True:
        try:
            user_input = input("You: ").strip()
            if not user_input:
                continue
            if user_input.lower() in ["exit", "quit"]:
                print("Goodbye!")
                break
        except (EOFError, KeyboardInterrupt):
            print("\nGoodbye!")
            break

        # 슬래시 명령어 처리
        if user_input.startswith("/"):
            parts = user_input.split()
            cmd = parts[0].lower()

            if cmd == "/add" and len(parts) >= 3:
                mcp_add(base_url, parts[1], parts[2])
            elif cmd == "/remove" and len(parts) >= 2:
                mcp_remove(base_url, parts[1])
            elif cmd == "/list":
                mcp_list(base_url)
            elif cmd == "/iter":
                if len(parts) >= 2:
                    try:
                        max_iterations = int(parts[1])
                        print(f"  max_iterations = {max_iterations}")
                    except ValueError:
                        print("  [Error] 숫자를 입력하세요")
                else:
                    print(f"  현재 max_iterations = {max_iterations}")
            elif cmd == "/clear":
                message_history.clear()
                print("  대화 내역 초기화됨")
            elif cmd == "/help":
                print_help()
            else:
                print(f"  알 수 없는 명령어: {cmd} (/help 참고)")
            print("-" * 40)
            continue

        # 대화 내역에 사용자 입력 추가
        message_history.append({"role": "user", "content": user_input})

        # JSON-RPC 2.0 요청
        payload = {
            "jsonrpc": "2.0",
            "method": "generate_content",
            "params": {
                "model": model,
                "messages": message_history,
                "max_iterations": max_iterations,
            },
            "id": str(uuid.uuid4()),
        }

        try:
            print("  (요청 중...)")
            response = requests.post(generate_url, json=payload, timeout=120)

            if response.status_code == 200:
                data = response.json()

                if data.get("error"):
                    err = data["error"]
                    print(f"  [RPC Error] code={err.get('code')}: {err.get('message')}")
                    if err.get("data"):
                        print(f"    detail: {err['data']}")
                    # 실패한 메시지는 내역에서 제거
                    message_history.pop()
                elif "result" in data:
                    result = data["result"]
                    generated_text = ""
                    if isinstance(result, dict):
                        generated_text = result.get("generated_text", "")
                    elif isinstance(result, str):
                        generated_text = result

                    if generated_text:
                        print(f"AI: {generated_text}")
                        message_history.append({"role": "assistant", "content": generated_text})
                    else:
                        print(f"  [Raw] {result}")
                else:
                    print(f"  [Unexpected] {data}")
            else:
                print(f"  [HTTP {response.status_code}] {response.text[:200]}")
                message_history.pop()

        except requests.exceptions.Timeout:
            print("  [Timeout] 응답 시간 초과 (120초)")
            message_history.pop()
        except requests.exceptions.ConnectionError:
            print(f"  [Error] 서버에 연결할 수 없습니다: {base_url}")
            print("  서버가 실행 중인지 확인하세요: uv run python main.py")
            message_history.pop()
        except Exception as e:
            print(f"  [Error] {e}")
            message_history.pop()

        print("-" * 40)


if __name__ == "__main__":
    main()
