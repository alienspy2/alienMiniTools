import os
import struct
import lz4.frame
from cryptography.hazmat.primitives.asymmetric import x25519
from cryptography.hazmat.primitives.kdf.hkdf import HKDF
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305
import nacl.signing
import nacl.exceptions

class CryptoContext:
    def __init__(self, key: bytes, nonce_base: bytes, role_is_sender: bool):
        self.aead = ChaCha20Poly1305(key)
        self.nonce_base = nonce_base
        self.counter = 0

    def encrypt(self, plaintext: bytes, aad: bytes = b"") -> bytes:
        # Nonce: nonce_base (4) + counter (8 Big Endian)
        nonce = self.nonce_base + struct.pack(">Q", self.counter)
        ciphertext = self.aead.encrypt(nonce, plaintext, aad)
        self.counter += 1
        return ciphertext

    def decrypt(self, ciphertext: bytes, aad: bytes = b"") -> bytes:
        nonce = self.nonce_base + struct.pack(">Q", self.counter)
        plaintext = self.aead.decrypt(nonce, ciphertext, aad)
        self.counter += 1
        return plaintext

def generate_identity_key() -> nacl.signing.SigningKey:
    """Generate a new Ed25519 identity key."""
    return nacl.signing.SigningKey.generate()

def generate_ephemeral_key() -> x25519.X25519PrivateKey:
    """Generate a new X25519 ephemeral key."""
    return x25519.X25519PrivateKey.generate()

def derive_session_keys(shared_secret: bytes, salt: bytes, info: bytes):
    """
    Derive session keys using HKDF-SHA256.
    Output: keys for both directions (c2s, s2c) and nonce bases.
    Total needs: 32+32+4+4 = 72 bytes
    """
    hkdf = HKDF(
        algorithm=hashes.SHA256(),
        length=72,
        salt=salt,
        info=info,
    )
    key_material = hkdf.derive(shared_secret)
    
    key_c2s = key_material[0:32]
    key_s2c = key_material[32:64]
    nonce_base_c2s = key_material[64:68]
    nonce_base_s2c = key_material[68:72]
    
    return key_c2s, key_s2c, nonce_base_c2s, nonce_base_s2c

def compress_data(data: bytes) -> bytes:
    return lz4.frame.compress(data)

def decompress_data(data: bytes, max_size: int) -> bytes:
    """
    압축 해제 결과가 과도하게 커지면 공격(압축 폭탄) 가능성이 있으므로
    상한을 넘는 경우 예외를 발생시킨다.
    """
    plaintext = lz4.frame.decompress(data)
    if len(plaintext) > max_size:
        raise ValueError(f"Decompressed data too large: {len(plaintext)} > {max_size}")
    return plaintext
