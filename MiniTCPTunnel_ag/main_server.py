import sys
import argparse
import asyncio
import logging

def main():
    parser = argparse.ArgumentParser(description="MiniTCPTunnel Server")
    parser.add_argument("--port", type=int, default=9000, help="Control channel listen port")
    parser.add_argument("--timeout", type=int, default=0, help="Auto shutdown after N seconds (for testing)")
    parser.add_argument("--verbose", action="store_true", help="Enable debug logs")
    args = parser.parse_args()

    # Windows 기본 인코딩(cp949)로 출력되면 로그가 깨질 수 있으므로,
    # stdout/stderr를 UTF-8로 재설정해 리다이렉션 파일도 UTF-8로 저장되게 한다.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        # 일부 환경에서는 reconfigure가 없을 수 있으니 안전하게 무시한다.
        pass

    # verbose 옵션에 따라 로그 상세 수준을 조정한다.
    log_level = logging.DEBUG if args.verbose else logging.INFO
    # Ensure logs are written to stdout so shell redirection captures them.
    logging.basicConfig(level=log_level, format="%(asctime)s [%(levelname)s] %(message)s", stream=sys.stdout)
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
