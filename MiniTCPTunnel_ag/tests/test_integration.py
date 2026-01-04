import asyncio
import logging
import sys
import os

# Add project root to sys.path
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

from mini_tcp_tunnel.server.core import Server
from mini_tcp_tunnel.server.protocol import ALLOWED_CLIENT_KEYS
from mini_tcp_tunnel.client.core import ControlClient, TunnelConfig
from mini_tcp_tunnel.shared.crypto_utils import generate_identity_key
import nacl.encoding

# Setup logging
logging.basicConfig(level=logging.INFO, format="%(name)s: %(message)s")

async def start_mock_local_service(port: int):
    """Simple Echo Server to simulate local service"""
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

    server = await asyncio.start_server(handle_echo, '127.0.0.1', port)
    logging.getLogger("MockService").info(f"Listening on {port}")
    return server

async def test_scenario():
    # 1. Setup Keys
    server_key = generate_identity_key()
    client_key = generate_identity_key()
    
    # White-list client key on server
    ALLOWED_CLIENT_KEYS.append(client_key.verify_key.encode())
    
    server_pub_bytes = server_key.verify_key.encode()

    # 2. Start Server
    server_port = 9900
    server = Server(server_port, server_key)
    server_task = asyncio.create_task(server.listen())
    
    await asyncio.sleep(0.5) # Wait for startup

    # 3. Start Mock Local Service
    local_service_port = 9980
    mock_service = await start_mock_local_service(local_service_port)

    # 4. Start Client
    client = ControlClient(
        server_host='127.0.0.1',
        server_port=server_port,
        identity_key=client_key,
        server_key=server_pub_bytes # Pinning
    )
    
    # Config Tunnel: Remote 9999 -> Local 9980
    remote_tunnel_port = 9999
    tunnel_cfg = TunnelConfig("test-tunnel", remote_tunnel_port, '127.0.0.1', local_service_port)
    client.add_tunnel(tunnel_cfg)
    
    # Connect
    await client.connect()
    
    # Wait for tunnel setup
    await asyncio.sleep(1)
    
    # 5. Verify Data Flow
    # Connect to Remote Tunnel Port (9999) and send data
    try:
        logging.info("--- Testing Traffic Flow ---")
        reader, writer = await asyncio.open_connection('127.0.0.1', remote_tunnel_port)
        
        test_msg = b"Hello Tunnel World!"
        writer.write(test_msg)
        await writer.drain()
        
        # Expect Echo
        response = await reader.read(100)
        logging.info(f"Received: {response}")
        
        if response == b"ECHO:" + test_msg:
            logging.info(">>> SUCCESS: Traffic echoed correctly! <<<")
        else:
            logging.error(f">>> FAIL: Expected 'ECHO:{test_msg}', got '{response}'")
            
        writer.close()
        await writer.wait_closed()
        
    except Exception as e:
        logging.error(f">>> FAIL: Connection error: {e}")

    # Cleanup
    logging.info("Cleaning up...")
    mock_service.close()
    if client.codec: await client.codec.close()
    server_task.cancel()
    try:
        await server_task
    except asyncio.CancelledError:
        pass

if __name__ == "__main__":
    try:
        # Use existing event loop if available (e.g. jupyter) or new one
        asyncio.run(test_scenario())
    except KeyboardInterrupt:
        pass
