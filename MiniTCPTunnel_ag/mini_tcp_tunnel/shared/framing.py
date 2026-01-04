import struct
import asyncio
from typing import Optional, Tuple
from .constants import FRAME_HEAD_LEN, MsgType, MAX_FRAME_LEN, MAX_PLAINTEXT_LEN
from .crypto_utils import CryptoContext, compress_data, decompress_data

class FrameCodec:
    """
    Handles framing, compression, and encryption.
    Format: | len (u32 BE) | ciphertext (AEAD) |
    """
    def __init__(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter, read_ctx: Optional[CryptoContext] = None, write_ctx: Optional[CryptoContext] = None):
        self.reader = reader
        self.writer = writer
        self.read_ctx = read_ctx
        self.write_ctx = write_ctx
        self.read_lock = asyncio.Lock()
        self.write_lock = asyncio.Lock()

    async def read_frame(self) -> bytes:
        async with self.read_lock:
            # Read length header
            try:
                head_data = await self.reader.readexactly(FRAME_HEAD_LEN)
            except asyncio.IncompleteReadError:
                raise ConnectionResetError("Connection closed while reading header")
                
            length = struct.unpack(">I", head_data)[0]
            # 비정상적으로 큰 프레임은 즉시 차단한다.
            if length > MAX_FRAME_LEN:
                raise ValueError(f"Frame too large: {length} > {MAX_FRAME_LEN}")
            
            # Read payload (ciphertext)
            try:
                ciphertext = await self.reader.readexactly(length)
            except asyncio.IncompleteReadError:
                raise ConnectionResetError("Connection closed while reading body")

            if self.read_ctx:
                # Decrypt -> Decompress
                compressed = self.read_ctx.decrypt(ciphertext)
                plaintext = decompress_data(compressed, MAX_PLAINTEXT_LEN)
                return plaintext
            else:
                return ciphertext

    async def write_frame(self, data: bytes):
        async with self.write_lock:
            if self.write_ctx:
                # 평문 길이가 과도하면 압축/암호화 전에 차단한다.
                if len(data) > MAX_PLAINTEXT_LEN:
                    raise ValueError(f"Plaintext too large: {len(data)} > {MAX_PLAINTEXT_LEN}")
                compressed = compress_data(data)
                ciphertext = self.write_ctx.encrypt(compressed)
                payload = ciphertext
            else:
                # 비암호화 모드에서도 프레임 길이는 제한한다.
                if len(data) > MAX_FRAME_LEN:
                    raise ValueError(f"Frame too large: {len(data)} > {MAX_FRAME_LEN}")
                payload = data


            length = len(payload)
            header = struct.pack(">I", length)
            
            self.writer.write(header + payload)
            await self.writer.drain()

    async def close(self):
        self.writer.close()
        try:
            await self.writer.wait_closed()
        except Exception:
            pass
