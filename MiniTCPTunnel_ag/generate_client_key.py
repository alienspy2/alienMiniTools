import logging
import nacl.encoding
from mini_tcp_tunnel.client.config_manager import ConfigManager

# Start Setup
logging.basicConfig(level=logging.INFO, format="%(message)s")

# Load or Create Config/Keys
cfg_mgr = ConfigManager("client_config.json")
cfg_mgr.load()
id_key = cfg_mgr.get_identity_key()

# Output Key
pub_hex = id_key.verify_key.encode(encoder=nacl.encoding.HexEncoder).decode('utf-8')
print("\n" + "="*60)
print(f"CLIENT PUBLIC KEY: {pub_hex}")
print("="*60)
print("\n[INSTRUCTION]")
print("Copy the above key string and register it in 'main_server.py'.")
print("Example:")
print(f'    add_allowed_client_key("{pub_hex}")')
print("="*60 + "\n")
