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

def test_jsonrpc():
    config = load_config()
    port = config.get("http_port", 20006)
    models = config.get("models", ["gemini-2.0-flash"])
    model = "gemma-3-4b-it"
    
    url = f"http://localhost:{port}/generate" # Endpoint is /generate based on main.py
    
    print(f"--- JSON-RPC Chat Client (Server: {url}) ---")
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

        # 대화 내역에 사용자 입력 추가
        message_history.append({"role": "user", "content": user_input})

        # JSON-RPC 2.0 요청
        # Server expects params to have 'messages' list
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
                        # Server returns {"generated_text": "...", "finish_reason": "..."}
                        generated_text = ""
                        if isinstance(result, dict):
                            if "generated_text" in result:
                                generated_text = result["generated_text"]
                                print(f"Gemini: {generated_text}")
                            elif "text" in result:
                                generated_text = result["text"]
                                print(f"Gemini: {generated_text}")
                            else:
                                print(f"Gemini (Raw Dict): {result}")
                        elif isinstance(result, str):
                            generated_text = result
                            print(f"Gemini: {generated_text}")
                        else:
                            print(f"Gemini (Raw): {result}")
                        
                        # 대화 내역에 어시스턴트 답변 추가
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
    test_jsonrpc()
