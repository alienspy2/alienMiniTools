import requests
import json

class OllamaClient:
    def __init__(self, model="gemma3:4b", base_url="http://localhost:11434"):
        self.model = model
        self.base_url = base_url
        self.api_generate = f"{self.base_url}/api/generate"

    def generate_script(self, user_prompt):
        system_prompt = (
            "You are a helpful assistant that translates user requests into Bash scripts. "
            "Output ONLY the code content of the bash script. "
            "Do not include markdown backticks (```bash ... ```). "
            "Do not include any explanations. "
            "Ensure the script is safe and correct."
        )
        
        full_prompt = f"{system_prompt}\n\nUser Request: {user_prompt}\nBash Script:"

        payload = {
            "model": self.model,
            "prompt": full_prompt,
            "stream": False
        }

        try:
            response = requests.post(self.api_generate, json=payload)
            response.raise_for_status()
            result = response.json()
            return result.get("response", "").strip()
        except requests.exceptions.RequestException as e:
            return f"# Error communicating with Ollama: {e}"
        except Exception as e:
            return f"# Error: {e}"
