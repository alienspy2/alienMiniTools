import asyncio
import logging
import subprocess
import sys
import time
import socket

# Config
SERVER_PORT = 9010
ECHO_PORT = 9980
TUNNEL_PORT = 9999
PYTHON_EXE = sys.executable

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")

async def start_echo_server():
    server = await asyncio.start_server(handle_echo, '127.0.0.1', ECHO_PORT)
    logging.info(f"Echo Server listening on {ECHO_PORT}")
    return server

async def handle_echo(reader, writer):
    try:
        while True:
            data = await reader.read(100)
            if not data: break
            writer.write(b"ECHO:" + data)
            await writer.drain()
    except Exception:
        pass
    finally:
        writer.close()

def run_process(cmd):
    return subprocess.Popen(cmd, stdout=sys.stdout, stderr=sys.stderr) # Pipe to main log

async def main():
    # 1. Start Echo Server
    echo_server = await start_echo_server()
    
    # 2. Start Tunnel Server
    logging.info("Starting Tunnel Server...")
    # Using --timeout 20 for safety
    p_server = run_process([PYTHON_EXE, "main_server.py", "--port", str(SERVER_PORT), "--timeout", "20"])
    
    await asyncio.sleep(2) # Wait for server startup

    # 3. Start Tunnel Client
    # Client loads config.json which has auto_start=True
    logging.info("Starting Tunnel Client...")
    # Use main_client.py (Headless mode would be better but PySide6 might open window. 
    # We ignore UI window for test or need headless flag. 
    # Assuming window open doesn't block logic if using qasync properly.)
    # Note: Test machine might show window.
    p_client = run_process([PYTHON_EXE, "main_client.py"])
    
    await asyncio.sleep(3) # Wait for client connect & tunnel setup

    # 4. Test Tunnel Traffic
    logging.info("Testing Tunnel Connection...")
    success = False
    try:
        reader, writer = await asyncio.open_connection('127.0.0.1', TUNNEL_PORT)
        
        msg = b"INTEGRATION_TEST"
        writer.write(msg)
        await writer.drain()
        
        data = await reader.read(1024)
        logging.info(f"Received: {data}")
        
        if data == b"ECHO:" + msg:
            logging.info(">>> SUCCESS: Tunnel works correctly! <<<")
            success = True
        else:
            logging.error(">>> FAIL: Echo mismatch")
            
        writer.close()
        await writer.wait_closed()
        
    except Exception as e:
        logging.error(f">>> FAIL: Connection error: {e}")

    # 5. Cleanup
    logging.info("Shutting down processes...")
    p_client.terminate()
    p_server.terminate()
    echo_server.close()
    await echo_server.wait_closed()
    
    if not success:
        sys.exit(1)

if __name__ == "__main__":
    asyncio.run(main())
