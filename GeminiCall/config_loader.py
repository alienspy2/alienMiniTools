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

    if 'providers' in config:
        _normalize_providers_format(config)
    else:
        _normalize_legacy_format(config)

    return config


def _normalize_legacy_format(config: dict):
    """기존 flat 포맷(api_key, models, rpm 최상위)을 providers 구조로 정규화."""
    required_keys = ['api_key', 'models', 'rpm', 'http_port']
    for key in required_keys:
        if key not in config:
            raise ValueError(f"Missing required config key: {key}")

    if not isinstance(config['models'], list):
        raise ValueError("Config key 'models' must be a list of strings")

    # providers 구조 생성
    config['providers'] = {
        'gemini': {
            'api_key': config['api_key'],
            'models': config['models'],
            'rpm': config['rpm'],
        }
    }
    config['all_models'] = list(config['models'])
    config['model_provider_map'] = {m: 'gemini' for m in config['models']}


def _normalize_providers_format(config: dict):
    """새 providers 포맷 검증 및 정규화."""
    if 'http_port' not in config:
        raise ValueError("Missing required config key: http_port")

    providers = config['providers']
    if not isinstance(providers, dict) or not providers:
        raise ValueError("Config key 'providers' must be a non-empty dict")

    all_models = []
    model_provider_map = {}

    for pname, pconf in providers.items():
        for key in ('api_key', 'models', 'rpm'):
            if key not in pconf:
                raise ValueError(f"Provider '{pname}' missing required key: {key}")
        if not isinstance(pconf['models'], list):
            raise ValueError(f"Provider '{pname}' models must be a list")

        for m in pconf['models']:
            if m in model_provider_map:
                raise ValueError(
                    f"Duplicate model '{m}' found in providers "
                    f"'{model_provider_map[m]}' and '{pname}'"
                )
            model_provider_map[m] = pname
            all_models.append(m)

    config['all_models'] = all_models
    config['model_provider_map'] = model_provider_map

    # 하위 호환: 첫 번째 프로바이더의 값을 최상위에 배치
    first_provider = next(iter(providers.values()))
    config.setdefault('api_key', first_provider['api_key'])
    config.setdefault('models', all_models)
    config.setdefault('rpm', first_provider['rpm'])
