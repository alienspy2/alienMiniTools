import asyncio
import logging
import struct
import base64
from typing import Dict, Optional, Set
from uuid import uuid4

from ..shared.constants import MsgType, DEFAULT_CONTROL_PORT
from ..shared.framing import FrameCodec
from .protocol import ServerHandshake, ALLOWED_CLIENT_KEYS, add_allowed_client_key
import nacl.signing

class ConnectionPairer:
    """
    Matches an incoming public connection (conn_id) with a client data channel.
    """
    def __init__(self):
        # conn_id -> Future[FrameCodec (the data channel)]
        self.pending: Dict[str, asyncio.Future] = {}
        # 디버깅을 위해 페어링 상태를 추적하는 전용 로거를 사용한다.
        self.logger = logging.getLogger("ConnectionPairer")

    def prepare(self, conn_id: str) -> asyncio.Future:
        loop = asyncio.get_running_loop()
        f = loop.create_future()
        self.pending[conn_id] = f
        # 대기 등록 시점에 현재 대기 수를 기록해 누락 여부를 확인한다.
        self.logger.debug(f"[페어링] 대기 등록 conn_id={conn_id}, pending={len(self.pending)}")
        return f

    def fulfill(self, conn_id: str, codec: FrameCodec):
        if conn_id in self.pending:
            self.pending[conn_id].set_result(codec)
            del self.pending[conn_id]
            # 정상 매칭 시점 로그: 데이터 채널 수신 확인용
            self.logger.debug(f"[페어링] 매칭 완료 conn_id={conn_id}, pending={len(self.pending)}")
        else:
            self.logger.warning(f"[페어링] 매칭 실패(만료/미등록) conn_id={conn_id}")
            asyncio.create_task(codec.close())

    def cancel(self, conn_id: str):
        if conn_id in self.pending:
            self.pending[conn_id].cancel()
            del self.pending[conn_id]
            # 타임아웃 등으로 취소되는 케이스를 추적한다.
            self.logger.debug(f"[페어링] 대기 취소 conn_id={conn_id}, pending={len(self.pending)}")

class PublicListener:
    """
    Listens on a public port and notifies the control session when a connection arrives.
    """
    def __init__(self, tunnel_id: str, local_port: int, control_session: 'ControlSession', pairer: ConnectionPairer):
        self.tunnel_id = tunnel_id
        self.local_port = local_port
        self.control_session = control_session
        self.pairer = pairer
        self.server: Optional[asyncio.Server] = None
        self.logger = logging.getLogger(f"PublicListener({local_port})")

    async def start(self):
        self.server = await asyncio.start_server(self.handle_conn, '0.0.0.0', self.local_port)
        self.logger.info(f"Tunnel {self.tunnel_id} listening on 0.0.0.0:{self.local_port}")

    async def stop(self):
        if self.server:
            srv = self.server
            self.server = None # Prevent double stop
            srv.close()
            await srv.wait_closed()
            self.logger.info(f"Stopped listener on {self.local_port}")

    async def handle_conn(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        conn_id = str(uuid4())[:8] # Short ID for debug readability
        peer = writer.get_extra_info('peername')
        self.logger.info(f"New public connection from {peer}, conn_id={conn_id}")

        # 1. Prepare pairing
        future = self.pairer.prepare(conn_id)

        # 2. Notify Client via Control Channel
        # Msg: INCOMING_CONN | tunnel_id_len(4) | tunnel_id | conn_id_len(4) | conn_id
        try:
            tid_bytes = self.tunnel_id.encode('utf-8')
            cid_bytes = conn_id.encode('utf-8')
            payload = struct.pack(">I", len(tid_bytes)) + tid_bytes + \
                      struct.pack(">I", len(cid_bytes)) + cid_bytes
            
            await self.control_session.send_message(MsgType.INCOMING_CONN, payload)
            # 제어 채널로 INCOMING_CONN 통지 완료를 기록한다.
            self.logger.debug(f"INCOMING_CONN sent: tunnel_id={self.tunnel_id}, conn_id={conn_id}")

            # 3. Wait for Data Channel from Client (timeout 10s)
            data_codec: FrameCodec = await asyncio.wait_for(future, timeout=10.0)
            
            # 4. Bridge Traffic
            self.logger.info(f"Bridging conn_id={conn_id} public <-> data channel")
            await self.bridge(reader, writer, data_codec)
            # 브리지가 정상 종료되면 흐름 종료를 기록한다.
            self.logger.debug(f"Bridge finished conn_id={conn_id}")

        except asyncio.TimeoutError:
            self.logger.error(f"Timeout waiting for data channel conn_id={conn_id}")
            self.pairer.cancel(conn_id)
        except Exception as e:
            self.logger.error(f"Error handling public conn {conn_id}: {e}")
        finally:
            # 소켓 종료 단계는 실패 원인 조사에 중요하므로 남긴다.
            self.logger.debug(f"Closing public socket conn_id={conn_id}")
            writer.close()
            await writer.wait_closed()

    async def bridge(self, public_r: asyncio.StreamReader, public_w: asyncio.StreamWriter, data_codec: FrameCodec):
        # Bridge loop: Public Raw <-> Data Codec (Encrypted/Compressed)
        # 디버깅을 위해 송수신 바이트 통계를 수집한다.
        # 너무 많은 로그를 피하기 위해 "첫 패킷"과 "종료 시 요약"만 기록한다.
        stats = {
            "public_to_client_bytes": 0,
            "public_to_client_packets": 0,
            "client_to_public_bytes": 0,
            "client_to_public_packets": 0,
        }
        
        async def pipe_public_to_client():
            try:
                while True:
                    data = await public_r.read(4096)
                    if not data: break
                    # 공용 포트 -> 데이터 채널 방향 트래픽 통계
                    stats["public_to_client_packets"] += 1
                    stats["public_to_client_bytes"] += len(data)
                    if stats["public_to_client_packets"] == 1:
                        self.logger.debug(f"첫 공용→클라이언트 데이터: {len(data)} bytes")
                    # Prepend Protocol Header: | Type(1) | Flags(1) | StreamID(4) |
                    header = bytes([MsgType.DATA, 0]) + struct.pack(">I", 0)
                    await data_codec.write_frame(header + data)
            except Exception as e:
                # 공용→클라이언트 경로에서 발생한 예외를 기록한다.
                # 여기서 예외가 나면 서버->클라이언트 데이터 전달이 끊길 수 있다.
                self.logger.exception(f"공용→클라이언트 파이프 예외: {e}")
        
        async def pipe_client_to_public():
            try:
                while True:
                    # Reads frame (Decrypted/Decompressed)
                    try:
                        data = await data_codec.read_frame()
                    except Exception as e:
                        # 복호화/압축 해제 실패 등은 여기서 발생할 수 있다.
                        # 실제 문제 원인 파악을 위해 예외를 상세 로그로 남긴다.
                        self.logger.exception(f"클라이언트→공용 read_frame 예외: {e}")
                        break
                    if not data: break # End of stream check? Length 0?
                    # Special check: Length 0 frame might be heartbeat or keepalive?
                    # FrameCodec returns plain bytes.
                    if len(data) == 0: continue
                    
                    # Wait, if we use MsgType for DATA, we need to strip it?
                    # In this architecture: Data Channel sends RAW frames of data?
                    # Plan 4.3 says "Frame Payload: Type | Flags | StreamID | Payload"
                    # BUT for Data Channel, if it's 1-to-1, do we need Type?
                    # If we use FrameCodec strictly as "Transport Layer", then inside is the Protocol.
                    # MsgType.DATA used?
                    # If we stick to "Protocol", then:
                    #   packet = read_frame()
                    #   type = packet[0]
                    #   if type == DATA: write(packet[body])
                    # Let's check parse logic below.
                    
                    if len(data) > 0 and data[0] == MsgType.DATA:
                        # Strip Type(1)+Flags(1)+StreamID(4) ?
                        # The Plan 4.3 defines headers.
                        # | type(1) | flags(1) | stream_id(4) | payload... |
                        # Data channel is 1-to-1, so StreamID might be 0.
                        # Let's assume header len is 1+1+4 = 6.
                        if len(data) > 6:
                            payload_len = len(data) - 6
                            stats["client_to_public_packets"] += 1
                            stats["client_to_public_bytes"] += payload_len
                            if stats["client_to_public_packets"] == 1:
                                self.logger.debug(f"첫 클라이언트→공용 데이터: {payload_len} bytes")
                            public_w.write(data[6:])
                            await public_w.drain()
                    elif len(data) > 0 and data[0] == MsgType.CLOSE_TUNNEL:
                         break
                    else:
                        # 예상하지 못한 타입은 로그로 남겨 디버깅에 활용한다.
                        self.logger.warning(f"클라이언트→공용 미지원 타입 수신: type={data[0]}")
            except Exception as e:
                # 클라이언트→공용 파이프에서 발생한 예외를 기록한다.
                self.logger.exception(f"클라이언트→공용 파이프 예외: {e}")

        task1 = asyncio.create_task(pipe_public_to_client())
        task2 = asyncio.create_task(pipe_client_to_public())
        
        # 주의: HTTP 클라이언트는 요청 전송 후 write 쪽을 먼저 닫는 경우가 있다.
        # 그때 공용→클라이언트 파이프가 먼저 종료되면, 응답 경로가 끊겨버린다.
        # 따라서 한쪽이 끝나도 반대쪽을 잠시 더 기다려 응답을 흘려보낸다.
        done, pending = await asyncio.wait([task1, task2], return_when=asyncio.FIRST_COMPLETED)
        if task1 in done and task2 not in done:
            self.logger.info("공용→클라이언트 종료, 클라이언트→공용 응답 대기 시작")
            try:
                await asyncio.wait_for(task2, timeout=10.0)
            except asyncio.TimeoutError:
                self.logger.warning("클라이언트→공용 응답 대기 시간 초과, 강제 종료")
                task2.cancel()
        elif task2 in done and task1 not in done:
            self.logger.info("클라이언트→공용 종료, 공용→클라이언트 종료 대기 시작")
            try:
                await asyncio.wait_for(task1, timeout=10.0)
            except asyncio.TimeoutError:
                self.logger.warning("공용→클라이언트 종료 대기 시간 초과, 강제 종료")
                task1.cancel()
        
        # 남아있는 태스크를 정리한다.
        await asyncio.gather(task1, task2, return_exceptions=True)
        
        # 브리지 종료 시 통계 요약 로그
        self.logger.debug(
            "브리지 통계: public->client packets=%d bytes=%d, client->public packets=%d bytes=%d",
            stats["public_to_client_packets"],
            stats["public_to_client_bytes"],
            stats["client_to_public_packets"],
            stats["client_to_public_bytes"],
        )
        await data_codec.close()

class ControlSession:
    def __init__(self, codec: FrameCodec, pairer: ConnectionPairer):
        self.codec = codec
        self.pairer = pairer
        # tunnel_id -> PublicListener
        self.tunnels: Dict[str, PublicListener] = {}
        self.logger = logging.getLogger("ControlSession")
        self.is_active = True

    async def send_message(self, msg_type: MsgType, payload: bytes = b""):
        # Construct packet: | Type(1) | Flags(1) | StreamID(4) | Payload |
        # Control channel uses StreamID 0 usually.
        header = bytes([msg_type, 0]) + struct.pack(">I", 0)
        await self.codec.write_frame(header + payload)

    async def send_tunnel_status(self, tunnel_id: str, status: str):
        """
        터널 상태를 클라이언트로 통보한다.
        페이로드 형식: | status_len(4) | status(utf-8) | tunnel_id_len(4) | tunnel_id(utf-8) |
        """
        status_bytes = status.encode('utf-8')
        tid_bytes = tunnel_id.encode('utf-8')
        payload = struct.pack(">I", len(status_bytes)) + status_bytes + \
                  struct.pack(">I", len(tid_bytes)) + tid_bytes
        # 상태 통보는 UI 동기화 문제 진단에 중요하므로 로그로 남긴다.
        self.logger.debug(f"Send TunnelStatus: tunnel_id={tunnel_id}, status={status}")
        await self.send_message(MsgType.TUNNEL_STATUS, payload)

    async def run(self):
        try:
            while self.is_active:
                data = await self.codec.read_frame()
                if not data: break
                
                # Parse | Type | Flags | StreamID | Payload |
                if len(data) < 6: continue
                msg_type = data[0]
                # flags = data[1]
                # stream_id = struct.unpack(">I", data[2:6])[0]
                payload = data[6:]

                await self.handle_message(msg_type, payload)
        except Exception as e:
            self.logger.error(f"Session error: {e}")
        finally:
            await self.cleanup()

    async def handle_message(self, msg_type: int, payload: bytes):
        if msg_type == MsgType.OPEN_TUNNEL:
            await self.handle_open_tunnel(payload)
        elif msg_type == MsgType.CLOSE_TUNNEL:
            await self.handle_close_tunnel(payload)
        elif msg_type == MsgType.DATA_CONN_READY:
             # This should NOT happen on Control Session typically if logic separates them early.
             # BUT if we reuse the handler, let's see. 
             # Wait, in 'Server.handle_client', we check the FIRST message.
             # If it's DATA_CONN_READY, we detach the codec and give it to Pairer.
             # So ControlSession only sees Control types.
             pass
        elif msg_type == MsgType.HEARTBEAT:
             # Echo back for keep-alive check
             if self.codec:
                 try:
                     # Send back raw heartbeat frame
                     # Payload is usually empty or timestamp, just echo it.
                     await self.send_message(MsgType.HEARTBEAT, payload)
                 except Exception:
                     pass

    async def handle_open_tunnel(self, payload: bytes):
        # Payload: | remote_port(4) | tunnel_id_len(4) | tunnel_id |
        tunnel_id = None
        try:
            remote_port = struct.unpack(">I", payload[0:4])[0]
            tid_len = struct.unpack(">I", payload[4:8])[0]
            tunnel_id = payload[8:8+tid_len].decode('utf-8')
            # 요청 수신 시점에 포트/ID를 기록해 적용 여부를 확인한다.
            self.logger.info(f"OpenTunnel 요청 수신: tunnel_id={tunnel_id}, port={remote_port}")
            
            # Check if exists
            if tunnel_id in self.tunnels:
                self.logger.warning(f"Tunnel {tunnel_id} already exists. Closing old one.")
                await self.tunnels[tunnel_id].stop()
                del self.tunnels[tunnel_id]

            listener = PublicListener(tunnel_id, remote_port, self, self.pairer)
            await listener.start()
            self.tunnels[tunnel_id] = listener
            
            # Send STATUS OK? For now just log
            self.logger.info(f"Tunnel opened: {tunnel_id} on port {remote_port}")
            # 클라이언트에게 "Open" 상태를 통보한다.
            await self.send_tunnel_status(tunnel_id, "Open")
            
            # Send Ack back?
            # await self.send_message(MsgType.TUNNEL_STATUS, ... ok ...)
        except Exception as e:
            self.logger.error(f"Failed to open tunnel: {e}")
            # 실패 시에도 상태를 알려준다. (UI에서 즉시 오류 표시 가능)
            try:
                if tunnel_id:
                    await self.send_tunnel_status(tunnel_id, "Error")
            except Exception:
                pass

    async def handle_close_tunnel(self, payload: bytes):
        # Payload: | tunnel_id_len(4) | tunnel_id |
        tunnel_id = None
        try:
            tid_len = struct.unpack(">I", payload[0:4])[0]
            tunnel_id = payload[4:4+tid_len].decode('utf-8')
            # 종료 요청 수신 시점에 ID를 기록한다.
            self.logger.info(f"CloseTunnel 요청 수신: tunnel_id={tunnel_id}")
            
            if tunnel_id in self.tunnels:
                await self.tunnels[tunnel_id].stop()
                del self.tunnels[tunnel_id]
                self.logger.info(f"Tunnel closed: {tunnel_id}")
                # 클라이언트에게 "Stopped" 상태를 통보한다.
                await self.send_tunnel_status(tunnel_id, "Stopped")
            else:
                self.logger.warning(f"Close request for unknown tunnel: {tunnel_id}")
                # 알 수 없는 터널이면 상태를 Error로 전달
                await self.send_tunnel_status(tunnel_id, "Error")
        except Exception as e:
            self.logger.error(f"Failed to close tunnel: {e}")
            # 예외 발생 시에도 상태 통보를 시도한다.
            try:
                if tunnel_id:
                    await self.send_tunnel_status(tunnel_id, "Error")
            except Exception:
                pass

    async def cleanup(self):
        self.is_active = False
        for t in self.tunnels.values():
            await t.stop()
        self.tunnels.clear()
        await self.codec.close()

class Server:
    def __init__(self, port: int, identity_key: nacl.signing.SigningKey):
        self.port = port
        self.identity_key = identity_key
        self.pairer = ConnectionPairer()
        self.logger = logging.getLogger("Server")

    async def listen(self):
        server = await asyncio.start_server(self.handle_client, '0.0.0.0', self.port)
        self.logger.info(f"Control Server listening on {self.port}")
        async with server:
            await server.serve_forever()

    async def handle_client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        peer = writer.get_extra_info('peername')
        self.logger.info(f"Connecting from {peer}")
        
        # 1. Handshake
        hs = ServerHandshake(reader, writer, self.identity_key)
        codec = await hs.perform_handshake()
        
        if not codec:
            self.logger.warning("Handshake failed. Closing.")
            writer.close()
            await writer.wait_closed()
            return

        # 2. Check First Packet for Intent (Control vs Data)
        try:
            first_frame_bytes = await codec.read_frame()
            if len(first_frame_bytes) < 6:
                # 헤더가 부족하면 정상 메시지로 볼 수 없어 종료한다.
                self.logger.warning("첫 프레임 길이가 너무 짧아 연결 종료")
                await codec.close()
                return
                
            msg_type = first_frame_bytes[0]
            # 첫 프레임 타입 로깅은 제어/데이터 채널 구분에 유용하다.
            self.logger.debug(f"첫 프레임 수신: msg_type={msg_type}")
            
            if msg_type == MsgType.DATA_CONN_READY:
                # Payload: conn_id_len | conn_id
                # Or just conn_id as string? Plan 4.4 says payload: conn_id.
                # Let's assume conn_id is proper string or bytes.
                # In PublicListener we pack it as: len(4) + bytes.
                # Let's stick to standard payload parsing.
                offset = 6 # skip header
                payload = first_frame_bytes[offset:]
                # Let's assume payload is directly 'conn_id' string (utf8)
                # Or parsing length-prefixed.
                # To be safe, let's treat payload as the conn_id string directly if no other fields.
                conn_id = payload.decode('utf-8')
                
                self.logger.info(f"Data Connection Ready for {conn_id}")
                self.pairer.fulfill(conn_id, codec)
                # Do NOT close codec here; it is handed off.
                
            else:
                # Assume Control Session (Main)
                # We need to process this first frame inside the session too?
                # Or just start session loop.
                # If msg_type is HELLO... wait, Handshake handles Hello.
                # The first frame AFTER handshake is what we are looking at.
                # It could be 'OpenTunnel' or 'Heartbeat'.
                
                session = ControlSession(codec, self.pairer)
                # We already read the first frame, we should handle it!
                await session.handle_message(msg_type, first_frame_bytes[6:])
                # Now enter loop
                await session.run()

        except Exception as e:
            self.logger.error(f"Error in client handler: {e}")
            await codec.close()
