
import json
import os
import codecs

CONFIG_PATH = 'config.json'

def load_config(path=CONFIG_PATH):
    if not os.path.exists(path):
        raise FileNotFoundError(f"Config file not found at {path}")
    
    # Read with utf-8-sig to handle BOM if present, or just utf-8
    with open(path, 'r', encoding='utf-8-sig') as f:
        try:
            config = json.load(f)
        except json.JSONDecodeError as e:
            raise ValueError(f"Invalid JSON format in {path}: {e}")
            
    required_keys = ['api_key', 'models', 'rpm', 'http_port']
    for key in required_keys:
        if key not in config:
            raise ValueError(f"Missing required config key: {key}")
            
    if not isinstance(config['models'], list):
        raise ValueError("Config key 'models' must be a list of strings")

    return config
