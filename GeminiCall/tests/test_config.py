
import pytest
import os
import json
import codecs
from config_loader import load_config

# Dummy config content
VALID_CONFIG = {
    "api_key": "test_key",
    "models": ["model-a", "model-b"],
    "rpm": 15,
    "http_port": 20006
}

@pytest.fixture
def config_file(tmp_path):
    d = tmp_path / "subdir"
    d.mkdir()
    p = d / "config.json"
    # Write with utf-8-sig to simulate BOM
    with open(p, 'w', encoding='utf-8-sig') as f:
        json.dump(VALID_CONFIG, f)
    return str(p)

def test_load_config_success(config_file):
    config = load_config(config_file)
    assert config['api_key'] == "test_key"
    assert config['models'] == ["model-a", "model-b"]
    assert config['rpm'] == 15
    assert config['http_port'] == 20006

def test_load_config_file_not_found():
    with pytest.raises(FileNotFoundError):
        load_config("non_existent_config.json")

def test_load_config_missing_keys(tmp_path):
    p = tmp_path / "bad_config.json"
    bad_conf = VALID_CONFIG.copy()
    del bad_conf['models']
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(bad_conf, f)
    
    with pytest.raises(ValueError, match="Missing required config key"):
        load_config(str(p))

def test_load_config_invalid_type(tmp_path):
    p = tmp_path / "type_error_config.json"
    bad_conf = VALID_CONFIG.copy()
    bad_conf['models'] = "just-string" # Should be list
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(bad_conf, f)
        
    with pytest.raises(ValueError, match="Config key 'models' must be a list"):
        load_config(str(p))
