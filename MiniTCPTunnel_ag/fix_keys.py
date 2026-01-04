import nacl.signing
import nacl.encoding

# Private Key from client_config.json
priv_hex = "60e865f12e8798e6c7ab4e652cb90a2bdd56b509d73d63321946002f254fdcf8"

# Derive Public Key
priv_key = nacl.signing.SigningKey(bytes.fromhex(priv_hex))
pub_key = priv_key.verify_key
pub_hex = pub_key.encode(encoder=nacl.encoding.HexEncoder).decode('utf-8')

print(f"Private: {priv_hex}")
print(f"Public : {pub_hex}")

# Update allowed_clients.txt
with open("allowed_clients.txt", "w") as f:
    f.write(f"{pub_hex}\n")
    
print("Updated allowed_clients.txt")
