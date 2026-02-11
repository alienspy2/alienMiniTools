import requests
import json
import os
import sys
import uuid

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

def test_gpt5nano():
    config = load_config()
    port = config.get("http_port", 20006)
    model = "gpt-5-nano"

    url = f"http://localhost:{port}/generate"

    print(f"--- GPT-5-nano Chat Client via GeminiCall (Server: {url}) ---")
    print(f"Using model: {model}")
    print("Type 'exit' or 'quit' to stop.\n")

    message_history = []

    while True:
        try:
            user_input = input("You: ").strip()
            if not user_input:
                continue
            if user_input.lower() in ["exit", "quit"]:
                print("Goodbye!")
                break
        except EOFError:
            break

        message_history.append({"role": "user", "content": user_input})

        payload = {
            "jsonrpc": "2.0",
            "method": "generate_content",
            "params": {
                "model": model,
                "messages": message_history
            },
            "id": str(uuid.uuid4())
        }

        try:
            response = requests.post(url, json=payload)

            if response.status_code == 200:
                try:
                    data = response.json()

                    if data.get("error"):
                        print(f"Server Error: {data['error']}")
                    elif "result" in data:
                        result = data["result"]
                        generated_text = ""
                        if isinstance(result, dict):
                            if "generated_text" in result:
                                generated_text = result["generated_text"]
                                print(f"GPT-5-nano: {generated_text}")
                            elif "text" in result:
                                generated_text = result["text"]
                                print(f"GPT-5-nano: {generated_text}")
                            else:
                                print(f"GPT-5-nano (Raw Dict): {result}")
                        elif isinstance(result, str):
                            generated_text = result
                            print(f"GPT-5-nano: {generated_text}")
                        else:
                            print(f"GPT-5-nano (Raw): {result}")

                        if generated_text:
                            message_history.append({"role": "assistant", "content": generated_text})

                except json.JSONDecodeError:
                    print(f"Error: Invalid JSON response\n{response.text}")
            else:
                print(f"HTTP Error {response.status_code}: {response.text}")

        except requests.exceptions.ConnectionError:
            print(f"Error: Could not connect to server at {url}. Is the server running?")
        except Exception as e:
            print(f"Error: {e}")

        print("-" * 20)

if __name__ == "__main__":
    test_gpt5nano()
