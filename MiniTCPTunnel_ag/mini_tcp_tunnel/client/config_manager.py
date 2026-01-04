import os
import json
import logging
from typing import List, Optional
import nacl.signing
import nacl.encoding
from pydantic import BaseModel
from ..shared.crypto_utils import generate_identity_key

CONFIG_FILE_NAME = "client_config.json"

class TunnelDefinition(BaseModel):
    id: str
    remote_port: int
    local_host: str = "127.0.0.1"
    local_port: int
    auto_start: bool = False

class ClientConfigModel(BaseModel):
    server_host: str = "127.0.0.1"
    server_port: int = 9000
    server_pub_key: Optional[str] = None
    identity_private_key_hex: Optional[str] = None
    tunnels: List[TunnelDefinition] = []

class ConfigManager:
    def __init__(self, path: str = CONFIG_FILE_NAME):
        self.path = path
        self.config = ClientConfigModel()

    def load(self):
        if os.path.exists(self.path):
            try:
                with open(self.path, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    self.config = ClientConfigModel(**data)
            except Exception as e:
                logging.error(f"Failed to load config: {e}")
        else:
            logging.info("Config file not found, creating default.")
            self.ensure_identity()
            self.save()

    def save(self):
        try:
            with open(self.path, 'w', encoding='utf-8') as f:
                f.write(self.config.model_dump_json(indent=2))
        except Exception as e:
            logging.error(f"Failed to save config: {e}")

    def ensure_identity(self) -> nacl.signing.SigningKey:
        if not self.config.identity_private_key_hex:
            logging.info("Generating new identity key...")
            key = generate_identity_key()
            hex_key = key.encode(encoder=nacl.encoding.HexEncoder).decode('utf-8')
            self.config.identity_private_key_hex = hex_key
            self.save()
            return key
        else:
            key_bytes = bytes.fromhex(self.config.identity_private_key_hex)
            return nacl.signing.SigningKey(key_bytes)

    def get_identity_key(self) -> nacl.signing.SigningKey:
        return self.ensure_identity()
