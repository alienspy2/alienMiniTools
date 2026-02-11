import pytest
import os
import json
import codecs
from config_loader import load_config

# Dummy config content (legacy flat format)
VALID_CONFIG = {
    "api_key": "test_key",
    "models": ["model-a", "model-b"],
    "rpm": 15,
    "http_port": 20006
}

# New providers format
VALID_PROVIDERS_CONFIG = {
    "providers": {
        "gemini": {
            "api_key": "gemini_key",
            "models": ["gemini-2.5-flash", "gemma-3-27b-it"],
            "rpm": 15
        },
        "openai": {
            "api_key": "openai_key",
            "models": ["gpt-4o", "gpt-4o-mini"],
            "rpm": 60
        }
    },
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


# --- New providers format tests ---

def test_providers_format_success(tmp_path):
    p = tmp_path / "config.json"
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(VALID_PROVIDERS_CONFIG, f)

    config = load_config(str(p))
    assert 'providers' in config
    assert 'gemini' in config['providers']
    assert 'openai' in config['providers']

    # all_models / model_provider_map
    assert set(config['all_models']) == {"gemini-2.5-flash", "gemma-3-27b-it", "gpt-4o", "gpt-4o-mini"}
    assert config['model_provider_map']['gpt-4o'] == 'openai'
    assert config['model_provider_map']['gemini-2.5-flash'] == 'gemini'

    # backward compat top-level keys
    assert config['api_key'] == "gemini_key"
    assert config['rpm'] == 15
    assert config['http_port'] == 20006

def test_legacy_format_auto_converts_to_providers(config_file):
    config = load_config(config_file)
    assert 'providers' in config
    assert 'gemini' in config['providers']
    assert config['providers']['gemini']['api_key'] == 'test_key'
    assert config['providers']['gemini']['models'] == ["model-a", "model-b"]
    assert config['all_models'] == ["model-a", "model-b"]
    assert config['model_provider_map'] == {"model-a": "gemini", "model-b": "gemini"}

def test_providers_duplicate_model_error(tmp_path):
    dup_config = {
        "providers": {
            "gemini": {
                "api_key": "k1",
                "models": ["shared-model"],
                "rpm": 10
            },
            "openai": {
                "api_key": "k2",
                "models": ["shared-model"],
                "rpm": 20
            }
        },
        "http_port": 20006
    }
    p = tmp_path / "config.json"
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(dup_config, f)

    with pytest.raises(ValueError, match="Duplicate model"):
        load_config(str(p))

def test_providers_missing_key(tmp_path):
    bad_config = {
        "providers": {
            "gemini": {
                "api_key": "k1",
                "models": ["m1"],
                # missing rpm
            }
        },
        "http_port": 20006
    }
    p = tmp_path / "config.json"
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(bad_config, f)

    with pytest.raises(ValueError, match="missing required key: rpm"):
        load_config(str(p))

def test_providers_missing_http_port(tmp_path):
    bad_config = {
        "providers": {
            "gemini": {
                "api_key": "k1",
                "models": ["m1"],
                "rpm": 10
            }
        }
    }
    p = tmp_path / "config.json"
    with open(p, 'w', encoding='utf-8') as f:
        json.dump(bad_config, f)

    with pytest.raises(ValueError, match="Missing required config key: http_port"):
        load_config(str(p))
