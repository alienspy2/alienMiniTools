import sys
import argparse
import asyncio
import logging

def main():
    parser = argparse.ArgumentParser(description="MiniTCPTunnel Server")
    parser.add_argument("--port", type=int, default=9000, help="Control channel listen port")
    parser.add_argument("--timeout", type=int, default=0, help="Auto shutdown after N seconds (for testing)")
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
    logging.info(f"Starting MiniTCPTunnel Server on port {args.port}...")

    from mini_tcp_tunnel.server.core import Server
    from mini_tcp_tunnel.shared.crypto_utils import generate_identity_key
    from mini_tcp_tunnel.server.protocol import add_allowed_client_key
    import nacl.encoding

    # Demo: Generate a random server identity key on startup
    # In production, this should be loaded from a file
    id_key = generate_identity_key()
    pub_hex = id_key.verify_key.encode(encoder=nacl.encoding.HexEncoder).decode('utf-8')
    # Load Allowed Keys from File
    allowed_keys_file = "allowed_clients.txt"
    try:
        with open(allowed_keys_file, "r") as f:
            for line in f:
                key = line.strip()
                if key and not key.startswith("#"):
                    add_allowed_client_key(key)
                    logging.info(f" -> Whitelisted Key: {key[:8]}...{key[-8:]}")
        logging.info(f"Loaded allowed keys from {allowed_keys_file}")
    except FileNotFoundError:
        logging.warning(f"{allowed_keys_file} not found. Creating empty file.")
        with open(allowed_keys_file, "w") as f:
            f.write("# Add Client Ed25519 Public Keys here (Hex format), one per line\n")
    
    server = Server(args.port, id_key)
    
    async def run_server():
        try:
            if args.timeout > 0:
                logging.info(f"Server will auto-shutdown in {args.timeout} seconds.")
                await asyncio.wait_for(server.listen(), timeout=args.timeout)
            else:
                await server.listen()
        except asyncio.TimeoutError:
            logging.info("Server verification timeout reached. Shutting down.")
        except Exception as e:
            logging.error(f"Server error: {e}")

    asyncio.run(run_server())

if __name__ == "__main__":
    main()
