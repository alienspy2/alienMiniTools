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
            # 핸드셰이크 시작 시점을 로그로 남겨 연결 흐름을 추적한다.
            self.logger.debug("클라이언트 핸드셰이크 시작")
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
            # 클라이언트 헬로 전송 완료
            self.logger.debug("ClientHello 전송 완료")

            # --- RECEIVE SERVER HELLO ---
            try:
                len_bytes = await self.reader.readexactly(4)
                resp_len = struct.unpack(">I", len_bytes)[0]
                server_hello_bytes = await self.reader.readexactly(resp_len)
            except asyncio.IncompleteReadError:
                self.logger.error("Handshake conn closed")
                return None

            # | Ver(2) | Role(1) | ID_Key(32) | Eph_Key(32) | Nonce(12) | Sig(64) |
            if len(server_hello_bytes) < 143:
                return None
                
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
            server_hello_body = server_hello_bytes[0:79]
            sig_payload = server_hello_body + client_sig
            
            verify_key = nacl.signing.VerifyKey(server_id_key_bytes)
            try:
                verify_key.verify(sig_payload, server_sig)
            except nacl.exceptions.BadSignatureError:
                self.logger.error("Bad signature from server")
                return None
            
            self.logger.info("Server identity verified.")
            # 핸드셰이크 성공 시점 로깅
            self.logger.debug("클라이언트 핸드셰이크 성공")

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
    def __init__(self, tid: str, remote_port: int, local_host: str, local_port: int, enabled: bool = True):
        self.tid = tid
        self.remote_port = remote_port
        self.local_host = local_host
        self.local_port = local_port
        self.status = "Stopped"
        self.enabled = enabled

    def __eq__(self, other):
        if not isinstance(other, TunnelConfig): return False
        return (self.tid == other.tid and 
                self.remote_port == other.remote_port and 
                self.local_host == other.local_host and 
                self.local_port == other.local_port)

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

        self.write_lock = asyncio.Lock()
        
        # Connection Manager
        self.should_reconnect = False
        self.connection_task = None
        
        # Heartbeat settings
        self.last_activity = 0.0
        self.heartbeat_task = None
        self.HEARTBEAT_INTERVAL = 30
        self.HEARTBEAT_TIMEOUT = 30 

        self.running_tunnels: Dict[str, TunnelConfig] = {} # Currently active on server
        
    def add_tunnel(self, config: TunnelConfig):
        # Just store in a list? 
        # ControlClient stores 'tunnels' as the "Goal State"? 
        # Or "tunnels" is just a registry?
        # Let's align: self.tunnels is the registry of ALL knowledge (Load from config).
        # self.running_tunnels is what we believe is OPEN on server.
        self.tunnels[config.tid] = config

    async def connect(self):
        """Starts the connection manager."""
        if self.should_reconnect: return # Already trying
        self.should_reconnect = True
        self.connection_task = asyncio.create_task(self.connection_loop())

    async def sync_tunnels(self, desired_configs: List[TunnelConfig]):
        """
        Syncs the server state to match the desired_configs (list).
        Only tunnels with .enabled=True in desired_configs will be opened.
        Others will be closed.
        """
        # 적용 대상과 동기화 상태를 추적하기 위해 상세 로그를 남긴다.
        self.logger.debug(f"Syncing tunnels... total={len(desired_configs)}")
        
        desired_map = {t.tid: t for t in desired_configs if t.enabled}
        self.logger.debug(f"Sync 대상(활성) 수: {len(desired_map)}")
        
        # 1. Identify tunnels to Close
        # Close if: NOT in desired_map OR (in desired_map but config changed)
        to_close = []
        for tid, run_cfg in list(self.running_tunnels.items()):
            if tid not in desired_map:
                to_close.append(tid)
            else:
                # Check for config changes (Remote/Local ports)
                target = desired_map[tid]
                if target != run_cfg:
                    self.logger.info(f"Tunnel {tid} configuration changed. Recreating.")
                    to_close.append(tid)

        for tid in to_close:
            # We use the running config to close
            if tid in self.running_tunnels:
                self.logger.debug(f"Close 대상: {tid}")
                await self.request_close_tunnel(self.running_tunnels[tid])
                # request_close_tunnel removes from running_tunnels? 
                # Currently it just sends msg. We should update state.
        
        # 2. Identify tunnels to Open
        for tid, target in desired_map.items():
            if tid not in self.running_tunnels:
                self.logger.debug(f"Open 대상: {tid} -> remote_port={target.remote_port}")
                await self.request_open_tunnel(target)

    async def request_open_tunnel(self, tunnel: TunnelConfig):
        # 제어 채널이 아직 준비되지 않았으면 요청을 보낼 수 없다.
        if not self.codec:
            # 연결 상태가 없을 때는 요청이 무시됨을 기록한다.
            self.logger.warning(f"OpenTunnel 요청 무시됨(연결 없음): {tunnel.tid}")
            return
        # 프로토콜 페이로드 구성: remote_port + tunnel_id 길이 + tunnel_id
        tid_bytes = tunnel.tid.encode('utf-8')
        payload = struct.pack(">I", tunnel.remote_port) + \
                  struct.pack(">I", len(tid_bytes)) + \
                  tid_bytes
        
        # 동시 전송 충돌을 막기 위해 write_lock으로 보호해 전송한다.
        async with self.write_lock:
            await self.codec.write_frame(bytes([MsgType.OPEN_TUNNEL, 0]) + struct.pack(">I", 0) + payload)
        
        # 요청 전송 완료 로그
        self.logger.info(f"Requested OpenTunnel {tunnel.tid} (port={tunnel.remote_port})")
        # UI/상태 갱신: 요청 상태로 전환하고 실행중 목록에 반영한다.
        tunnel.status = "Requested"
        self.running_tunnels[tunnel.tid] = tunnel
        
        if self.on_tunnel_status_change: self.on_tunnel_status_change(tunnel.tid, "Requested")

    async def request_close_tunnel(self, tunnel: TunnelConfig):
        # 제어 채널이 준비되지 않은 경우 종료 요청을 보낼 수 없다.
        if not self.codec:
            # 연결 상태가 없을 때는 요청이 무시됨을 기록한다.
            self.logger.warning(f"CloseTunnel 요청 무시됨(연결 없음): {tunnel.tid}")
            return
        # 프로토콜 페이로드 구성: tunnel_id 길이 + tunnel_id
        tid_bytes = tunnel.tid.encode('utf-8')
        payload = struct.pack(">I", len(tid_bytes)) + tid_bytes
        
        # 동시 전송 충돌을 막기 위해 write_lock으로 보호해 전송한다.
        async with self.write_lock:
            await self.codec.write_frame(bytes([MsgType.CLOSE_TUNNEL, 0]) + struct.pack(">I", 0) + payload)
        
        # 요청 전송 완료 로그
        self.logger.info(f"Requested CloseTunnel {tunnel.tid}")
        # UI/상태 갱신: 종료 상태로 전환하고 실행중 목록에서 제거한다.
        tunnel.status = "Stopped"
        
        if tunnel.tid in self.running_tunnels:
            del self.running_tunnels[tunnel.tid]
            
        if self.on_tunnel_status_change: self.on_tunnel_status_change(tunnel.tid, "Stopped")

    async def disconnect(self):
        """Stops the connection manager and closes connection."""
        self.should_reconnect = False
        
        if self.connection_task:
            self.connection_task.cancel()
            try:
                await self.connection_task
            except asyncio.CancelledError:
                pass
            self.connection_task = None

        await self._close_connection()
        if self.on_status_change: self.on_status_change("Disconnected")

    async def _close_connection(self):
        self.is_connected = False
        if self.heartbeat_task:
            self.heartbeat_task.cancel()
            try:
                await self.heartbeat_task
            except asyncio.CancelledError:
                pass
            self.heartbeat_task = None
            
        if self.codec:
            await self.codec.close()
            self.codec = None

    async def connection_loop(self):
        self.logger.info("Connection Manager Started")
        while self.should_reconnect:
            if not self.is_connected:
                success = await self._perform_connect()
                if not success:
                    # Retry Countdown
                    if not self.should_reconnect: break
                    
                    retry_seconds = 30
                    for i in range(retry_seconds, 0, -1):
                        if not self.should_reconnect or self.is_connected: break
                        if self.on_status_change: 
                            self.on_status_change(f"Retry in {i}s...")
                        await asyncio.sleep(1)
            else:
                # Already connected, sleep and check logic is handled by loop/heartbeat
                await asyncio.sleep(1)

    async def _perform_connect(self) -> bool:
        try:
            self.logger.info(f"Connecting to {self.server_host}:{self.server_port}...")
            if self.on_status_change: self.on_status_change("Connecting...")
            
            # Timeout for connection attempt
            try:
                reader, writer = await asyncio.wait_for(
                    asyncio.open_connection(self.server_host, self.server_port), 
                    timeout=10
                )
            except (asyncio.TimeoutError, OSError) as e:
                self.logger.error(f"Conn Error: {e}")
                if self.on_status_change: self.on_status_change("Connection Failed")
                return False

            self.logger.info(f"Connecting from {writer.get_extra_info('sockname')}")
            # 연결 시점 로깅은 방화벽/라우팅 문제 확인에 유용하다.
            self.logger.debug("TCP 연결 성립, 핸드셰이크 진행")
            
            hs = ClientHandshake(reader, writer, self.identity_key, self.server_key)
            self.codec = await hs.perform_handshake()
            
            if self.codec:
                self.is_connected = True
                self.last_activity = asyncio.get_event_loop().time()
                self.logger.info("Handshake Success")
                if self.on_status_change: self.on_status_change("Connected")
                
                # Start Tasks
                self.heartbeat_task = asyncio.create_task(self.heartbeat_loop())
                asyncio.create_task(self.loop())
                
                # Sync Tunnels (Apply current config)
                await self.sync_tunnels(list(self.tunnels.values()))
                return True
            else:
                self.logger.error("Handshake failed")
                if self.on_status_change: self.on_status_change("Handshake Failed")
                writer.close()
                await writer.wait_closed()
                return False
                
        except Exception as e:
            self.logger.error(f"Connection failed: {e}")
            if self.on_status_change: self.on_status_change("Error")
            return False

    async def heartbeat_loop(self):
        try:
            while self.is_connected:
                await asyncio.sleep(self.HEARTBEAT_INTERVAL)
                if not self.is_connected: break
                
                now = asyncio.get_event_loop().time()
                
                # Check Timeout
                if now - self.last_activity > (self.HEARTBEAT_INTERVAL + self.HEARTBEAT_TIMEOUT):
                    self.logger.error("Heartbeat Timeout! Server is silent.")
                    await self._close_connection() # Just close, let connection_loop handle reconnect
                    return 

                # Send Heartbeat
                if self.codec:
                    try:
                        async with self.write_lock:
                             await self.codec.write_frame(bytes([MsgType.HEARTBEAT, 0]) + struct.pack(">I", 0))
                    except Exception as e:
                        self.logger.error(f"Heartbeat send failed: {e}")
                        await self._close_connection()
                        return
                        
        except asyncio.CancelledError:
            pass
        except Exception as e:
            self.logger.error(f"Heartbeat loop error: {e}")
            await self._close_connection()

    async def loop(self):
        try:
            while self.is_connected and self.codec:
                try:
                    plaintext = await self.codec.read_frame()
                    self.last_activity = asyncio.get_event_loop().time() # Update activity on ANY data
                except (ConnectionError, ConnectionResetError) as e:
                    self.logger.error(f"Control loop error: {e}")
                    break
                
                if not plaintext: break 
                # 프로토콜 헤더(1+1+4=6바이트)가 최소 길이
                if len(plaintext) < 6:
                    # 비정상 프레임은 버리되, 진단용 로그를 남긴다.
                    self.logger.warning("제어 채널 프레임 길이 부족으로 폐기")
                    continue
                
                msg_type = plaintext[0]
                # Payload는 헤더(타입/플래그/스트림ID)를 제외한 영역이다.
                payload = plaintext[6:]
                await self.handle_message(msg_type, payload)
        except Exception as e:
            self.logger.error(f"Loop Exception: {e}")
        finally:
            self.logger.info("Loop ended")
            # We don't call disconnect() here because it stops the manager.
            # We just close the connection state.
            # The manager will see is_connected=False and retry.
            await self._close_connection()

    async def handle_message(self, msg_type: int, payload: bytes):
        if msg_type == MsgType.INCOMING_CONN:
            await self.handle_incoming_conn(payload)
        elif msg_type == MsgType.TUNNEL_STATUS:
            await self.handle_tunnel_status(payload)
        elif msg_type == MsgType.HEARTBEAT:
            # Just Pong receiving. Activity already updated.
            # self.logger.debug("Received Heartbeat Echo")
            pass
        else:
            self.logger.warning(f"Received unknown message type: {msg_type}")

    async def handle_tunnel_status(self, payload: bytes):
        """
        서버가 보내는 터널 상태 메시지를 파싱해 UI에 반영한다.
        페이로드 형식: | status_len(4) | status(utf-8) | tunnel_id_len(4) | tunnel_id(utf-8) |
        """
        try:
            if len(payload) < 8:
                self.logger.warning("TunnelStatus payload too short")
                return
            
            status_len = struct.unpack(">I", payload[0:4])[0]
            offset = 4
            status = payload[offset:offset+status_len].decode('utf-8')
            offset += status_len
            
            tid_len = struct.unpack(">I", payload[offset:offset+4])[0]
            offset += 4
            tunnel_id = payload[offset:offset+tid_len].decode('utf-8')
            # 수신한 상태 정보를 로깅해 UI 업데이트 여부를 확인한다.
            self.logger.debug(f"TunnelStatus 수신: tunnel_id={tunnel_id}, status={status}")

            # 내부 상태 갱신 (있다면)
            if tunnel_id in self.tunnels:
                self.tunnels[tunnel_id].status = status

            # 실행중 목록은 상태에 맞게 유지한다.
            if status.lower() in ["open", "active"]:
                if tunnel_id in self.tunnels:
                    self.running_tunnels[tunnel_id] = self.tunnels[tunnel_id]
            elif status.lower() in ["stopped", "closed", "error"]:
                if tunnel_id in self.running_tunnels:
                    del self.running_tunnels[tunnel_id]

            # UI 콜백 갱신
            if self.on_tunnel_status_change:
                self.on_tunnel_status_change(tunnel_id, status)
                
        except Exception as e:
            self.logger.error(f"TunnelStatus parse error: {e}")

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

            asyncio.create_task(self.spawn_data_channel(tunnel, conn_id))
            # 데이터 채널 생성 요청을 기록한다.
            self.logger.debug(f"Data channel spawn requested: tunnel_id={tid}, conn_id={conn_id}")
            
        except Exception as e:
            self.logger.error(f"Parse incoming conn error: {e}")

    async def spawn_data_channel(self, tunnel: TunnelConfig, conn_id: str):
        try:
            # 1. Connect to Server (Data Channel)
            self.logger.debug(f"Data channel 연결 시작: conn_id={conn_id}")
            s_reader, s_writer = await asyncio.open_connection(self.server_host, self.server_port)
            
            # 2. Handshake for Data Channel
            hs = ClientHandshake(s_reader, s_writer, self.identity_key, self.server_key)
            codec = await hs.perform_handshake()
            if not codec:
                self.logger.error("Data channel handshake failed")
                return
            self.logger.debug(f"Data channel handshake 완료: conn_id={conn_id}")
            
            # 3. Send DATA_CONN_READY
            payload = conn_id.encode('utf-8')
            header = bytes([MsgType.DATA_CONN_READY, 0]) + struct.pack(">I", 0)
            await codec.write_frame(header + payload)
            # 데이터 채널 준비 완료 통지 로그
            self.logger.debug(f"DATA_CONN_READY 전송: conn_id={conn_id}")
            
            # 4. Connect to Local Target
            self.logger.info(f"Connecting to local target {tunnel.local_host}:{tunnel.local_port}")
            try:
                l_reader, l_writer = await asyncio.open_connection(tunnel.local_host, tunnel.local_port)
            except Exception as e:
                self.logger.error(f"Failed to connect local: {e}")
                await codec.close()
                return
            self.logger.info(f"Local target 연결 성공: {tunnel.local_host}:{tunnel.local_port}")
            
            # 5. Bridge
            self.logger.debug(f"브리지 시작: conn_id={conn_id}")
            await self.bridge_data(codec, l_reader, l_writer)
            self.logger.debug(f"브리지 종료: conn_id={conn_id}")
            
        except Exception as e:
            self.logger.error(f"Spawn data channel error: {e}")

    async def bridge_data(self, server_codec: FrameCodec, local_r: asyncio.StreamReader, local_w: asyncio.StreamWriter):
        # 데이터 채널 브리지 함수 진입 로그
        self.logger.debug("bridge_data 진입")
        # 디버깅을 위해 송수신 바이트 통계를 수집한다.
        # 너무 많은 로그를 피하기 위해 "첫 패킷"과 "종료 시 요약"만 기록한다.
        stats = {
            "server_to_local_bytes": 0,
            "server_to_local_packets": 0,
            "local_to_server_bytes": 0,
            "local_to_server_packets": 0,
        }
        async def pipe_server_to_local():
            try:
                while True:
                    try:
                        data = await server_codec.read_frame()
                    except Exception as e:
                        # 서버→로컬 경로에서 복호화/압축 해제 예외를 기록한다.
                        self.logger.exception(f"서버→로컬 read_frame 예외: {e}")
                        break
                    if not data: break
                    if len(data) == 0: continue
                    
                    if len(data) > 6:
                         # Plaintext checks (Type, Flags, StreamID)
                         # We assume Type(1)+Flags(1)+StreamID(4) = 6 bytes header
                         # Payload starts at 6
                         payload_len = len(data) - 6
                         stats["server_to_local_packets"] += 1
                         stats["server_to_local_bytes"] += payload_len
                         if stats["server_to_local_packets"] == 1:
                             self.logger.debug(f"첫 서버→로컬 데이터: {payload_len} bytes")
                         local_w.write(data[6:])
                         await local_w.drain()
            except Exception:
                # 서버→로컬 파이프 예외는 로컬 응답이 전달되지 않는 원인이 될 수 있다.
                self.logger.exception("서버→로컬 파이프 예외")
        
        async def pipe_local_to_server():
            try:
                while True:
                    data = await local_r.read(4096)
                    if not data: break
                    stats["local_to_server_packets"] += 1
                    stats["local_to_server_bytes"] += len(data)
                    if stats["local_to_server_packets"] == 1:
                        self.logger.debug(f"첫 로컬→서버 데이터: {len(data)} bytes")
                    # Wrap with DATA Type
                    header = bytes([MsgType.DATA, 0]) + struct.pack(">I", 0)
                    # FrameCodec 내부에서 이미 write_lock을 사용하므로 여기서는 중복 잠금 금지.
                    # 중복 잠금은 데이터 채널에서 데드락을 만들 수 있다.
                    try:
                        await server_codec.write_frame(header + data)
                    except Exception as e:
                        # 로컬→서버 전송 실패는 터널 응답이 사라지는 원인이 된다.
                        self.logger.exception(f"로컬→서버 write_frame 예외: {e}")
                        break
            except Exception:
                # 로컬→서버 파이프 예외 로그
                self.logger.exception("로컬→서버 파이프 예외")

        task_server_to_local = asyncio.create_task(pipe_server_to_local())
        task_local_to_server = asyncio.create_task(pipe_local_to_server())
        
        # 주의: 로컬 서비스가 응답 후 먼저 종료되는 경우가 있다.
        # 이때 다른 방향의 데이터를 끊지 않도록 반대쪽을 잠시 더 기다린다.
        done, pending = await asyncio.wait(
            [task_server_to_local, task_local_to_server],
            return_when=asyncio.FIRST_COMPLETED
        )
        if task_local_to_server in done and task_server_to_local not in done:
            self.logger.debug("로컬→서버 종료, 서버→로컬 종료 대기 시작")
            try:
                await asyncio.wait_for(task_server_to_local, timeout=10.0)
            except asyncio.TimeoutError:
                self.logger.warning("서버→로컬 종료 대기 시간 초과, 강제 종료")
                task_server_to_local.cancel()
        elif task_server_to_local in done and task_local_to_server not in done:
            self.logger.debug("서버→로컬 종료, 로컬→서버 종료 대기 시작")
            try:
                await asyncio.wait_for(task_local_to_server, timeout=10.0)
            except asyncio.TimeoutError:
                self.logger.warning("로컬→서버 종료 대기 시간 초과, 강제 종료")
                task_local_to_server.cancel()

        # 남아있는 태스크를 정리한다.
        await asyncio.gather(task_server_to_local, task_local_to_server, return_exceptions=True)
        # 브리지 종료 시점 로그
        self.logger.debug("bridge_data 종료")
        # 브리지 종료 시 통계 요약 로그
        self.logger.debug(
            "브리지 통계: server->local packets=%d bytes=%d, local->server packets=%d bytes=%d",
            stats["server_to_local_packets"],
            stats["server_to_local_bytes"],
            stats["local_to_server_packets"],
            stats["local_to_server_bytes"],
        )
        
        local_w.close()
        await server_codec.close()
