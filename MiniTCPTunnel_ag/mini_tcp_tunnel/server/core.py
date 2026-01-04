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

    def prepare(self, conn_id: str) -> asyncio.Future:
        loop = asyncio.get_running_loop()
        f = loop.create_future()
        self.pending[conn_id] = f
        return f

    def fulfill(self, conn_id: str, codec: FrameCodec):
        if conn_id in self.pending:
            self.pending[conn_id].set_result(codec)
            del self.pending[conn_id]
        else:
            logging.warning(f"Received DataConnReady for unknown/expired conn_id: {conn_id}")
            asyncio.create_task(codec.close())

    def cancel(self, conn_id: str):
        if conn_id in self.pending:
            self.pending[conn_id].cancel()
            del self.pending[conn_id]

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
            self.server.close()
            await self.server.wait_closed()
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

            # 3. Wait for Data Channel from Client (timeout 10s)
            data_codec: FrameCodec = await asyncio.wait_for(future, timeout=10.0)
            
            # 4. Bridge Traffic
            self.logger.info(f"Bridging conn_id={conn_id} public <-> data channel")
            await self.bridge(reader, writer, data_codec)

        except asyncio.TimeoutError:
            self.logger.error(f"Timeout waiting for data channel conn_id={conn_id}")
            self.pairer.cancel(conn_id)
        except Exception as e:
            self.logger.error(f"Error handling public conn {conn_id}: {e}")
        finally:
            writer.close()
            await writer.wait_closed()

    async def bridge(self, public_r: asyncio.StreamReader, public_w: asyncio.StreamWriter, data_codec: FrameCodec):
        # Bridge loop: Public Raw <-> Data Codec (Encrypted/Compressed)
        
        async def pipe_public_to_client():
            try:
                while True:
                    data = await public_r.read(4096)
                    if not data: break
                    # Prepend Protocol Header: | Type(1) | Flags(1) | StreamID(4) |
                    header = bytes([MsgType.DATA, 0]) + struct.pack(">I", 0)
                    await data_codec.write_frame(header + data)
            except Exception:
                pass
        
        async def pipe_client_to_public():
            try:
                while True:
                    # Reads frame (Decrypted/Decompressed)
                    data = await data_codec.read_frame()
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
                            public_w.write(data[6:])
                            await public_w.drain()
                    elif len(data) > 0 and data[0] == MsgType.CLOSE_TUNNEL:
                         break
            except Exception:
                pass

        task1 = asyncio.create_task(pipe_public_to_client())
        task2 = asyncio.create_task(pipe_client_to_public())
        
        done, pending = await asyncio.wait([task1, task2], return_when=asyncio.FIRST_COMPLETED)
        for t in pending: t.cancel()
        
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
        try:
            remote_port = struct.unpack(">I", payload[0:4])[0]
            tid_len = struct.unpack(">I", payload[4:8])[0]
            tunnel_id = payload[8:8+tid_len].decode('utf-8')
            
            # Check if exists
            if tunnel_id in self.tunnels:
                self.logger.warning(f"Tunnel {tunnel_id} already exists. Closing old one.")
                await self.tunnels[tunnel_id].stop()

            listener = PublicListener(tunnel_id, remote_port, self, self.pairer)
            await listener.start()
            self.tunnels[tunnel_id] = listener
            
            # Send STATUS OK? For now just log
            self.logger.info(f"Tunnel opened: {tunnel_id} on port {remote_port}")
            
            # Send Ack back?
            # await self.send_message(MsgType.TUNNEL_STATUS, ... ok ...)
        except Exception as e:
            self.logger.error(f"Failed to open tunnel: {e}")

    async def handle_close_tunnel(self, payload: bytes):
        # Payload: | tunnel_id_len(4) | tunnel_id |
        try:
            tid_len = struct.unpack(">I", payload[0:4])[0]
            tunnel_id = payload[4:4+tid_len].decode('utf-8')
            
            if tunnel_id in self.tunnels:
                await self.tunnels[tunnel_id].stop()
                del self.tunnels[tunnel_id]
                self.logger.info(f"Tunnel closed: {tunnel_id}")
            else:
                self.logger.warning(f"Close request for unknown tunnel: {tunnel_id}")
        except Exception as e:
            self.logger.error(f"Failed to close tunnel: {e}")

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
                await codec.close()
                return
                
            msg_type = first_frame_bytes[0]
            
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
