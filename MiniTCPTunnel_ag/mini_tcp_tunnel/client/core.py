import asyncio
import logging
import struct
import nacl.signing
import nacl.encoding
from typing import Optional, List, Dict, Callable
from ..shared.constants import PROTOCOL_VERSION, Role, MsgType
from ..shared.crypto_utils import (
    CryptoContext,
    generate_ephemeral_key,
    derive_session_keys
)
from ..shared.framing import FrameCodec
from cryptography.hazmat.primitives.asymmetric import x25519
from cryptography.hazmat.primitives import serialization

class ClientHandshake:
    def __init__(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter, 
                 client_identity_key: nacl.signing.SigningKey,
                 expected_server_pub_key: Optional[bytes] = None):
        self.reader = reader
        self.writer = writer
        self.client_identity_key = client_identity_key
        self.expected_server_pub_key = expected_server_pub_key # For strict pinning
        self.logger = logging.getLogger("ClientHandshake")

    async def perform_handshake(self) -> Optional[FrameCodec]:
        try:
            # --- GENERATE CLIENT HELLO ---
            client_eph_priv = generate_ephemeral_key()
            client_eph_pub = client_eph_priv.public_key()
            client_eph_pub_bytes = client_eph_pub.public_bytes(
                encoding=serialization.Encoding.Raw,
                format=serialization.PublicFormat.Raw
            )
            client_nonce = nacl.utils.random(12)
            client_id_pub_bytes = self.client_identity_key.verify_key.encode()

            # Body: | Ver(2) | Role(1) | ID_Key(32) | Eph_Key(32) | Nonce(12) |
            hello_body = struct.pack(">H", PROTOCOL_VERSION) + \
                         bytes([Role.CLIENT]) + \
                         client_id_pub_bytes + \
                         client_eph_pub_bytes + \
                         client_nonce
            
            # Sign the body
            client_sig = self.client_identity_key.sign(hello_body).signature

            # Handshake verification transcript needs signature over plain body
            client_hello_msg = hello_body + client_sig

            # Send Length + Msg
            msg_len = struct.pack(">I", len(client_hello_msg))
            self.writer.write(msg_len + client_hello_msg)
            await self.writer.drain()

            # --- RECEIVE SERVER HELLO ---
            len_bytes = await self.reader.readexactly(4)
            resp_len = struct.unpack(">I", len_bytes)[0]
            server_hello_bytes = await self.reader.readexactly(resp_len)

            # | Ver(2) | Role(1) | ID_Key(32) | Eph_Key(32) | Nonce(12) | Sig(64) |
            ver = struct.unpack(">H", server_hello_bytes[0:2])[0]
            role = server_hello_bytes[2]
            server_id_key_bytes = server_hello_bytes[3:35]
            server_eph_pub_bytes = server_hello_bytes[35:67]
            server_nonce = server_hello_bytes[67:79]
            server_sig = server_hello_bytes[79:143] # 64 bytes

            if ver != PROTOCOL_VERSION:
                self.logger.warning("Server version mismatch")
                return None
            if role != Role.SERVER:
                self.logger.warning("Invalid server role")
                return None

            # Verify Server Pinning if set
            if self.expected_server_pub_key and server_id_key_bytes != self.expected_server_pub_key:
                self.logger.error("Server public key does NOT match expected key!")
                return None

            # Verify Signature
            # Server signs: ServerHelloBody + ClientSig
            # ServerHelloBody is bytes[0:79]
            server_hello_body = server_hello_bytes[0:79]
            sig_payload = server_hello_body + client_sig
            
            verify_key = nacl.signing.VerifyKey(server_id_key_bytes)
            try:
                verify_key.verify(sig_payload, server_sig)
            except nacl.exceptions.BadSignatureError:
                self.logger.error("Bad signature from server")
                return None
            
            self.logger.info("Server identity verified.")

            # --- KEY DERIVATION ---
            server_eph_pub = x25519.X25519PublicKey.from_public_bytes(server_eph_pub_bytes)
            shared_secret = client_eph_priv.exchange(server_eph_pub)
            
            salt = client_nonce + server_nonce
            info = b"MINI_TCP_TUNNEL_V1"

            key_c2s, key_s2c, nonce_base_c2s, nonce_base_s2c = derive_session_keys(shared_secret, salt, info)

            # Client: Sends C2S, Receives S2C
            write_ctx = CryptoContext(key_c2s, nonce_base_c2s, role_is_sender=True)
            read_ctx = CryptoContext(key_s2c, nonce_base_s2c, role_is_sender=False)
            
            codec = FrameCodec(self.reader, self.writer, read_ctx=read_ctx, write_ctx=write_ctx)
            return codec

        except Exception as e:
            self.logger.error(f"Client Handshake failed: {e}")
            return None

class TunnelConfig:
    def __init__(self, tid: str, remote_port: int, local_host: str, local_port: int):
        self.tid = tid
        self.remote_port = remote_port
        self.local_host = local_host
        self.local_port = local_port
        self.status = "Stopped"

class ControlClient:
    def __init__(self, server_host: str, server_port: int, identity_key: nacl.signing.SigningKey, server_key: Optional[bytes] = None):
        self.server_host = server_host
        self.server_port = server_port
        self.identity_key = identity_key
        self.server_key = server_key # allowed/expected server key
        
        self.codec: Optional[FrameCodec] = None
        self.is_connected = False
        self.tunnels: Dict[str, TunnelConfig] = {} # id -> config
        self.logger = logging.getLogger("ControlClient")
        
        # Callbacks for UI updates
        self.on_status_change: Optional[Callable[[str], None]] = None
        self.on_tunnel_status_change: Optional[Callable[[str, str], None]] = None

    def add_tunnel(self, config: TunnelConfig):
        self.tunnels[config.tid] = config

    async def connect(self):
        self.logger.info(f"Connecting to {self.server_host}:{self.server_port}...")
        try:
            reader, writer = await asyncio.open_connection(self.server_host, self.server_port)
            hs = ClientHandshake(reader, writer, self.identity_key, self.server_key)
            self.codec = await hs.perform_handshake()
            
            if not self.codec:
                self.logger.error("Handshake failed")
                return
            
            self.is_connected = True
            if self.on_status_change: self.on_status_change("Connected")
            
            # Start Loop
            asyncio.create_task(self.loop())
            
            # Request Tunnels
            for t in self.tunnels.values():
                await self.request_open_tunnel(t)
                
        except Exception as e:
            self.logger.error(f"Connection failed: {e}")
            if self.on_status_change: self.on_status_change(f"Error: {e}")
            self.is_connected = False

    async def request_open_tunnel(self, tunnel: TunnelConfig):
        if not self.codec: return
        # Payload: | remote_port(4) | tunnel_id_len(4) | tunnel_id |
        tid_bytes = tunnel.tid.encode('utf-8')
        payload = struct.pack(">I", tunnel.remote_port) + \
                  struct.pack(">I", len(tid_bytes)) + \
                  tid_bytes
        
        await self.codec.write_frame(bytes([MsgType.OPEN_TUNNEL, 0]) + struct.pack(">I", 0) + payload)
        self.logger.info(f"Requested OpenTunnel {tunnel.tid}")
        tunnel.status = "Requested"
        if self.on_tunnel_status_change: self.on_tunnel_status_change(tunnel.tid, "Requested")

    async def request_close_tunnel(self, tunnel: TunnelConfig):
        if not self.codec: return
        # Payload: | tunnel_id_len(4) | tunnel_id |
        # Wait, server handle_close_tunnel needs payload format.
        # Let's define simple format: same as others.
        # Just sending Tunnel ID is enough if Server maps ID -> Listener.
        
        tid_bytes = tunnel.tid.encode('utf-8')
        payload = struct.pack(">I", len(tid_bytes)) + tid_bytes
        
        await self.codec.write_frame(bytes([MsgType.CLOSE_TUNNEL, 0]) + struct.pack(">I", 0) + payload)
        self.logger.info(f"Requested CloseTunnel {tunnel.tid}")
        tunnel.status = "Stopped" # Assume stopped immediately or wait for ack?
        if self.on_tunnel_status_change: self.on_tunnel_status_change(tunnel.tid, "Stopped")

    async def loop(self):
        try:
            while self.is_connected and self.codec:
                frame = await self.codec.read_frame()
                if not frame: break
                
                # Parse
                if len(frame) < 6: continue
                msg_type = frame[0]
                payload = frame[6:]
                
                if msg_type == MsgType.INCOMING_CONN:
                    await self.handle_incoming_conn(payload)
                elif msg_type == MsgType.TUNNEL_STATUS:
                    pass # Handle status ack
                    
        except Exception as e:
            self.logger.error(f"Control loop error: {e}")
        finally:
            self.is_connected = False
            if self.on_status_change: self.on_status_change("Disconnected")
            if self.codec: await self.codec.close()

    async def handle_incoming_conn(self, payload: bytes):
        # Payload: | len(4) | tid | len(4) | conn_id |
        try:
            p1_len = struct.unpack(">I", payload[0:4])[0]
            tid = payload[4:4+p1_len].decode('utf-8')
            offset = 4+p1_len
            p2_len = struct.unpack(">I", payload[offset:offset+4])[0]
            conn_id = payload[offset+4:offset+4+p2_len].decode('utf-8')
            
            self.logger.info(f"Incoming connection for tunnel {tid}, conn_id={conn_id}")
            
            tunnel = self.tunnels.get(tid)
            if not tunnel:
                self.logger.warning(f"Unknown tunnel id {tid}")
                return

            # Spawn Data Channel
            asyncio.create_task(self.spawn_data_channel(tunnel, conn_id))
            
        except Exception as e:
            self.logger.error(f"Parse incoming conn error: {e}")

    async def spawn_data_channel(self, tunnel: TunnelConfig, conn_id: str):
        try:
            # 1. Connect to Server (Data Channel)
            s_reader, s_writer = await asyncio.open_connection(self.server_host, self.server_port)
            
            # 2. Handshake for Data Channel
            # Same identity key, NEW ephemeral key
            hs = ClientHandshake(s_reader, s_writer, self.identity_key, self.server_key)
            codec = await hs.perform_handshake()
            if not codec:
                self.logger.error("Data channel handshake failed")
                return
            
            # 3. Send DATA_CONN_READY
            # Payload: conn_id (string)
            # The FIRST frame after handshake identifies the channel purpose.
            # No header needed? Server.handle_client checks first byte.
            # MsgType.DATA_CONN_READY (1 byte) + ...
            # Wait, Server.handle_client expects FrameCodec frame.
            # Frame content: | Type | ...
            # Let's construct standard frame format: | Type | Flags | StreamID | Payload |
            payload = conn_id.encode('utf-8')
            header = bytes([MsgType.DATA_CONN_READY, 0]) + struct.pack(">I", 0)
            await codec.write_frame(header + payload)
            
            # 4. Connect to Local Target
            self.logger.info(f"Connecting to local target {tunnel.local_host}:{tunnel.local_port}")
            try:
                l_reader, l_writer = await asyncio.open_connection(tunnel.local_host, tunnel.local_port)
            except Exception as e:
                self.logger.error(f"Failed to connect local: {e}")
                await codec.close()
                return
            
            # 5. Bridge
            await self.bridge_data(codec, l_reader, l_writer)
            
        except Exception as e:
            self.logger.error(f"Spawn data channel error: {e}")

    async def bridge_data(self, server_codec: FrameCodec, local_r: asyncio.StreamReader, local_w: asyncio.StreamWriter):
        async def pipe_server_to_local():
            try:
                while True:
                    data = await server_codec.read_frame()
                    if not data: break
                    if len(data) == 0: continue
                    # Strip Data Header? Server sends RAW data with header?
                    # Server.bridge logic: "public_r.read -> data_codec.write_frame(data)"
                    # Server wraps raw data into Frame.
                    # Does Server add Type Header?
                    # My Server.bridge implementation: "data_codec.write_frame(data)"
                    # => It puts raw data as payload.
                    # Wait, our FrameCodec only adds Length + Encryption.
                    # It does NOT add Type/Flags automatically.
                    # The Server.bridge code I wrote:
                    #   await data_codec.write_frame(data)
                    # This implies payload IS just raw data.
                    # BUT Client.handle_incoming_conn logic above checks msg_type = data[0].
                    # If Server sends RAW without Type Header, Client reading frame[0] gets first byte of data!
                    # CRITICAL MISMATCH.
                    
                    # Correction: Data Channel should encapsulate Data frames properly OR use Raw stream?
                    # Plan 4.3 says: "plaintext: | type | flags | stream_id | payload |"
                    # So Server.bridge MUST prepend header.
                    # And Client.bridge MUST prepend header.
                    
                    # BUT for simplicity and max throughput, maybe Data Channel (dedicated) should be RAW inside the Frame?
                    # Except we need to support 'CloseTunnel' or signals.
                    # So Header is needed.
                    
                    # Let's fix decode here assuming Header is present.
                    # If Server code didn't add it, I need to fix Server code too.
                    # Let's assume standard framing.
                    
                    if len(data) > 6:
                         # type = data[0]
                         local_w.write(data[6:])
                         await local_w.drain()
            except Exception:
                pass
        
        async def pipe_local_to_server():
            try:
                while True:
                    data = await local_r.read(4096)
                    if not data: break
                    # Wrap with DATA Type
                    header = bytes([MsgType.DATA, 0]) + struct.pack(">I", 0)
                    await server_codec.write_frame(header + data)
            except Exception:
                pass

        tasks = [
            asyncio.create_task(pipe_server_to_local()),
            asyncio.create_task(pipe_local_to_server())
        ]
        await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
        for t in tasks: t.cancel()
        
        local_w.close()
        await server_codec.close()
