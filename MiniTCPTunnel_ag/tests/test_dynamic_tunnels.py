import asyncio
import logging
import sys
import os

# Path hack
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

from mini_tcp_tunnel.server.core import Server
from mini_tcp_tunnel.server.protocol import ALLOWED_CLIENT_KEYS
from mini_tcp_tunnel.client.core import ControlClient, TunnelConfig
from mini_tcp_tunnel.shared.crypto_utils import generate_identity_key

def log(msg):
    print(msg, flush=True)

async def start_mock_local_service(port: int):
    async def handle_echo(reader, writer):
        try:
            while True:
                data = await reader.read(100)
                if not data: break
                writer.write(b"ECHO:" + data)
                await writer.drain()
        except:
            pass
        finally:
            writer.close()
    return await asyncio.start_server(handle_echo, '127.0.0.1', port)

async def test_dynamic_tunnels():
    log(">>> [TEST] STARTING TEST")
    
    server_key = generate_identity_key()
    client_key = generate_identity_key()
    ALLOWED_CLIENT_KEYS.append(client_key.verify_key.encode())
    
    server_port = 9022
    
    # --- Server Start ---
    log(">>> [TEST] Starting Server")
    server = Server(server_port, server_key)
    server_task = asyncio.create_task(server.listen())
    await asyncio.sleep(0.5)

    # --- Client Start ---
    log(">>> [TEST] Starting Client")
    client = ControlClient(
        server_host='127.0.0.1', 
        server_port=server_port, 
        identity_key=client_key,
        server_key=server_key.verify_key.encode()
    )
    
    try:
        await client.connect()
        log(">>> [TEST] Client Handshake Done")
    except Exception as e:
        log(f">>> [TEST] Client Connect Error: {e}")
        return

    # Client Loop Task
    loop_task = asyncio.create_task(client.loop())
    await asyncio.sleep(0.1)
    
    # --- Mock Service ---
    mock = await start_mock_local_service(9984)

    # --- Scenario 1: OPEN ---
    tunnel_cfg = TunnelConfig("dyn-2", 10003, '127.0.0.1', 9984)
    client.add_tunnel(tunnel_cfg)
    
    log(">>> [TEST] 1. Requesting Open Tunnel")
    await client.request_open_tunnel(tunnel_cfg)
    await asyncio.sleep(1.0) # Wait for server
    
    try:
        log(">>> [TEST] Testing Connection to 10003")
        reader, writer = await asyncio.wait_for(asyncio.open_connection('127.0.0.1', 10003), timeout=2.0)
        writer.write(b"HELLO-DYN2")
        await writer.drain()
        resp = await asyncio.wait_for(reader.read(100), timeout=2.0)
        log(f">>> [TEST] Received: {resp}")
        assert resp == b"ECHO:HELLO-DYN2"
        writer.close()
        await writer.wait_closed()
        log(">>> [TEST] OPEN SUCCESS")
    except Exception as e:
        log(f">>> [TEST] OPEN FAIL: {e}")
        # Dont exit yet, try cleanup

    # --- Scenario 2: CLOSE ---
    log(">>> [TEST] 2. Requesting Close Tunnel")
    await client.request_close_tunnel(tunnel_cfg)
    await asyncio.sleep(1.0) 
    
    try:
        reader, writer = await asyncio.wait_for(asyncio.open_connection('127.0.0.1', 10003), timeout=2.0)
        log(">>> [TEST] CLOSE FAIL: Port 10003 is still open!")
        writer.close()
    except (ConnectionRefusedError, OSError):
        log(">>> [TEST] CLOSE SUCCESS: Connection Refused (Correct)")
    except asyncio.TimeoutError:
        log(">>> [TEST] CLOSE SUCCESS: Connection Timeout (Likely Dropped)")
    except Exception as e:
        log(f">>> [TEST] CLOSE SUCCESS? Unexpected: {e}")

    # --- Cleanup ---
    log(">>> [TEST] Cleaning up...")
    mock.close()
    
    # Close Client
    log(">>> [TEST] Closing Client Codec...")
    if client.codec:
        await client.codec.close() # This should break the loop
    
    log(">>> [TEST] Cancelling Loop Task...")
    loop_task.cancel()
    try:
        await loop_task
    except asyncio.CancelledError:
        log(">>> [TEST] Loop Task Cancelled")
        
    log(">>> [TEST] Cancelling Server Task...")
    server_task.cancel()
    try:
        await server_task
    except asyncio.CancelledError:
        pass
        
    log(">>> [TEST] DONE")

if __name__ == "__main__":
    try:
        if sys.platform == 'win32':
             asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
        asyncio.run(test_dynamic_tunnels())
    except KeyboardInterrupt:
        pass
