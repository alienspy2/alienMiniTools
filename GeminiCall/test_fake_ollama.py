import requests
import json
import os
import sys

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

def test_ollama():
    config = load_config()
    port = config.get("http_port", 20006)
    models = config.get("models", ["gemma-3-4b-it"])
    model = "gemma-3-4b-it" 
    
    url = f"http://localhost:{port}/api/generate"
    
    print(f"--- Fake Ollama Generate Client (Server: {url}) ---")
    print(f"Using model: {model}")
    print("Type 'exit' or 'quit' to stop.\n")

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

        # Ollama 호환 요청
        payload = {
            "model": model,
            "prompt": user_input,
            "stream": False
        }
        
        try:
            response = requests.post(url, json=payload)
            
            if response.status_code == 200:
                try:
                    data = response.json()
                    # Ollama generate returns 'response' field
                    if "response" in data:
                        print(f"Gemini: {data['response']}")
                    else:
                        print(f"Gemini (Raw JSON): {json.dumps(data, indent=2, ensure_ascii=False)}")
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
    test_ollama()
