import requests
import json
import os

def test_single():
    url = "http://localhost:20006/api/generate"
    payload = {
        "model": "gemini-2.0-flash",
        "prompt": "안녕",
        "stream": False
    }
    print(f"Sending request to {url}...")
    try:
        response = requests.post(url, json=payload)
        print(f"Status: {response.status_code}")
        print(f"Response: {response.text}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    test_single()
