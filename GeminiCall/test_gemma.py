import json
import os
from google import genai

# config.json 로드
script_dir = os.path.dirname(os.path.abspath(__file__))
config_path = os.path.join(script_dir, "config.json")

try:
    with open(config_path, "r", encoding="utf-8") as f:
        config = json.load(f)
        api_key = config.get("api_key")
        if not api_key:
            raise ValueError("API Key not found in config.json")
except FileNotFoundError:
    print(f"Error: config.json not found at {config_path}")
    exit(1)
except Exception as e:
    print(f"Error loading config.json: {e}")
    exit(1)

# API 키 설정
client = genai.Client(api_key=api_key)

# Gemma 3 모델 호출 (예: 27B IT 모델)
response = client.models.generate_content(
    model="gemma-3-27b-it", 
    contents="Gemma 3의 주요 특징을 짧게 설명해줘."
)

print(response.text)
