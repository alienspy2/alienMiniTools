import asyncio
import logging
import struct
import nacl.signing
import nacl.encoding
from typing import Optional, List
from ..shared.constants import PROTOCOL_VERSION, Role, MsgType
from ..shared.crypto_utils import (
    CryptoContext,
    generate_ephemeral_key,
    derive_session_keys
)
from ..shared.framing import FrameCodec
from cryptography.hazmat.primitives.asymmetric import x25519
from cryptography.hazmat.primitives import serialization

# In-memory whitelist for demo purposes. In real app, load from config/file.
# Format: List of VerifyKey bytes (32 bytes)
ALLOWED_CLIENT_KEYS: List[bytes] = []

def add_allowed_client_key(pubkey_hex: str):
    try:
        key_bytes = bytes.fromhex(pubkey_hex)
        if len(key_bytes) != 32:
            logging.error("Invalid key length")
            return
        ALLOWED_CLIENT_KEYS.append(key_bytes)
        logging.info(f"Added authorized client key: {pubkey_hex}")
    except Exception as e:
        logging.error(f"Failed to add key: {e}")

class ServerHandshake:
    def __init__(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter, server_identity_key: nacl.signing.SigningKey):
        self.reader = reader
        self.writer = writer
        self.server_identity_key = server_identity_key
        self.logger = logging.getLogger("ServerHandshake")

    async def perform_handshake(self) -> Optional[FrameCodec]:
        """
        Performs the server-side handshake.
        Returns a configured FrameCodec if successful, None otherwise.
        """
        try:
            # 1. Receive Client Hello
            # Format assumption: | len(4) | JSON or Struct |
            # For simplicity in this demo, we use a binary struct for Hello
            # | ProtocolVer(2) | Role(1) | IdentityKey(32) | EphemeralKey(32) | Nonce(12) | Signature(64) |
            # Wait, signature covers what? Usually "client_hello_params" + "server_hello_params" (if mutual).
            # Plan says: "Hello exchange".
            # Let's simplify: Client sends Hello first. Server verifies. Server sends Hello.
            
            # Read Client Hello (Fixed size for simplicity: 2+1+32+32+12+64 = 143 bytes)
            # Actually, let's just assume simple framing for Hello too.
            
            # --- RECEIVE CLIENT HELLO ---
            # To simplify framing, we assume the first packet is sent raw or with simple length prefix without encryption.
            # Let's use 4-byte length prefix for handshake messages too.
            
            len_bytes = await self.reader.readexactly(4)
            msg_len = struct.unpack(">I", len_bytes)[0]
            client_hello_bytes = await self.reader.readexactly(msg_len)
            
            # Parse Client Hello
            # | Ver(2) | Role(1) | ID_Key(32) | Eph_Key(32) | Nonce(12) |
            # We verify signature LATER or now? Plan says "Transcript".
            # Let's do: Client -> Server: | Ver | Role | ID_Key | Eph_Key | Nonce | Sig_of_This_Msg |
            
            ver = struct.unpack(">H", client_hello_bytes[0:2])[0]
            role = client_hello_bytes[2]
            client_id_key_bytes = client_hello_bytes[3:35]
            client_eph_pub_bytes = client_hello_bytes[35:67]
            client_nonce = client_hello_bytes[67:79]
            client_sig = client_hello_bytes[79:143] # 64 bytes
            
            if ver != PROTOCOL_VERSION:
                self.logger.warning(f"Version mismatch: {ver}")
                return None
                
            if role != Role.CLIENT:
                self.logger.warning("Invalid role in Hello")
                return None

            # Verify Client Identity (Allow-list check)
            if client_id_key_bytes not in ALLOWED_CLIENT_KEYS:
                self.logger.warning("Client identity not in whitelist.")
                # Send AuthFail?
                # For security, maybe silent close or generic error.
                # await self._send_error(MsgType.AUTH_FAIL)
                return None
            
            # Verify Signature
            signed_data = client_hello_bytes[0:79]
            verify_key = nacl.signing.VerifyKey(client_id_key_bytes)
            try:
                verify_key.verify(signed_data, client_sig)
            except nacl.exceptions.BadSignatureError:
                self.logger.error("Bad signature from client")
                return None
                
            self.logger.info("Client signature verified. Identity authorized.")

            # --- GENERATE SERVER HELLO ---
            # Server Identity & Ephemeral
            server_eph_priv = generate_ephemeral_key()
            server_eph_pub = server_eph_priv.public_key()
            server_eph_pub_bytes = server_eph_pub.public_bytes(
                encoding=serialization.Encoding.Raw,
                format=serialization.PublicFormat.Raw
            )
            
            server_nonce = nacl.utils.random(12)
            server_id_pub_bytes = self.server_identity_key.verify_key.encode()
            
            # Construct body
            # | Ver(2) | Role(1) | ID_Key(32) | Eph_Key(32) | Nonce(12) |
            resp_body = struct.pack(">H", PROTOCOL_VERSION) + \
                        bytes([Role.SERVER]) + \
                        server_id_pub_bytes + \
                        server_eph_pub_bytes + \
                        server_nonce
            
            # Sign the response body + Client Hello signature (to bind session)
            # Transcript binding: Sign(ServerHelloBody + ClientSig)
            sig_payload = resp_body + client_sig
            server_sig = self.server_identity_key.sign(sig_payload).signature
            
            server_hello_msg = resp_body + server_sig
            
            # Send Server Hello
            resp_len = struct.pack(">I", len(server_hello_msg))
            self.writer.write(resp_len + server_hello_msg)
            await self.writer.drain()
            
            # --- KEY DERIVATION ---
            client_eph_pub = x25519.X25519PublicKey.from_public_bytes(client_eph_pub_bytes)
            shared_secret = server_eph_priv.exchange(client_eph_pub)
            
            # Salt: client_nonce + server_nonce
            salt = client_nonce + server_nonce
            info = b"MINI_TCP_TUNNEL_V1"
            
            key_c2s, key_s2c, nonce_base_c2s, nonce_base_s2c = derive_session_keys(shared_secret, salt, info)
            
            # Prepare Crypto Contexts (Server: Receives c2s, Sends s2c)
            # Server Codec needs to decrypt using c2s, encrypt using s2c
            # Wait, CryptoContext usually handles ONE direction or logic needs update?
            # Our CryptoContext is a single object.
            # FrameCodec might need distinct contexts for Read and Write?
            # Yes, standard AEAD usage.
            
            # Let's update CryptoContext to be single direction or FrameCodec to take two.
            # Simplify: FrameCodec takes read_ctx and write_ctx.
            
            read_ctx = CryptoContext(key_c2s, nonce_base_c2s, role_is_sender=False)
            write_ctx = CryptoContext(key_s2c, nonce_base_s2c, role_is_sender=True)
            
            # Create properly configured Codec
            codec = FrameCodec(self.reader, self.writer, read_ctx=read_ctx, write_ctx=write_ctx)
            
            return codec
            
        except Exception as e:
            self.logger.error(f"Handshake failed: {e}")
            return None
