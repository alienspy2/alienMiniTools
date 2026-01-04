import struct
import asyncio
from typing import Optional, Tuple
from .constants import FRAME_HEAD_LEN, MsgType
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

    async def read_frame(self) -> bytes:
        # Read length header
        try:
            head_data = await self.reader.readexactly(FRAME_HEAD_LEN)
        except asyncio.IncompleteReadError:
            raise ConnectionResetError("Connection closed while reading header")
            
        length = struct.unpack(">I", head_data)[0]
        
        # Read payload (ciphertext)
        try:
            ciphertext = await self.reader.readexactly(length)
        except asyncio.IncompleteReadError:
            raise ConnectionResetError("Connection closed while reading body")

        if self.read_ctx:
            # Decrypt -> Decompress
            compressed = self.read_ctx.decrypt(ciphertext)
            plaintext = decompress_data(compressed)
            return plaintext
        else:
            return ciphertext

    async def write_frame(self, data: bytes):
        if self.write_ctx:
            compressed = compress_data(data)
            ciphertext = self.write_ctx.encrypt(compressed)
            payload = ciphertext
        else:
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
