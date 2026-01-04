import sys
import argparse
import logging
import asyncio
from PySide6.QtWidgets import QApplication
import qasync

def main():
    parser = argparse.ArgumentParser(description="MiniTCPTunnel Client")
    parser.add_argument("--config", type=str, default="client.json", help="Path to configuration file")
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
    logging.info("Starting MiniTCPTunnel Client...")

    # PySide6 + asyncio integration using qasync
    app = QApplication(sys.argv)
    loop = qasync.QEventLoop(app)
    asyncio.set_event_loop(loop)

    from mini_tcp_tunnel.client.ui.main_window import MainWindow
    from mini_tcp_tunnel.client.ui.state import AppState, TunnelViewModel
    from mini_tcp_tunnel.client.core import ControlClient, TunnelConfig
    from mini_tcp_tunnel.client.config_manager import ConfigManager
    import nacl.encoding
    
    # 1. Load Config
    cfg_mgr = ConfigManager("client_config.json")
    cfg_mgr.load()
    
    # Ensure ID Key
    id_key = cfg_mgr.get_identity_key()
    pub_hex = id_key.verify_key.encode(encoder=nacl.encoding.HexEncoder).decode('utf-8')
    logging.info(f"Client Identity Public Key (Add this to Server): {pub_hex}")

    # 2. Setup State & Tunnels from Config
    state = AppState()
    state.server_host = cfg_mgr.config.server_host
    state.server_port = cfg_mgr.config.server_port

    # 서버 공개키 핀닝: 사용자가 수동으로 복사한 server_pub_key(hex)를 검증에 사용한다.
    server_pub_key_bytes = None
    if cfg_mgr.config.server_pub_key:
        try:
            server_pub_key_bytes = bytes.fromhex(cfg_mgr.config.server_pub_key)
            if len(server_pub_key_bytes) != 32:
                logging.error("server_pub_key 길이가 올바르지 않습니다. (32 bytes 필요)")
                server_pub_key_bytes = None
        except ValueError:
            logging.error("server_pub_key 형식이 올바르지 않습니다. (hex 문자열 필요)")
            server_pub_key_bytes = None
    
    # Populate Tunnels
    for t_def in cfg_mgr.config.tunnels:
        vm = TunnelViewModel(
            t_def.id, 
            t_def.remote_port, 
            t_def.local_host, 
            t_def.local_port,
            enabled=t_def.auto_start
        )
        state.tunnels.append(vm)

    if not state.tunnels:
        # Add default example if empty
        logging.info("No tunnels found in config. Adding example.")
        ex_tunnel = TunnelViewModel("example-web", 8080, "127.0.0.1", 8000)
        state.tunnels.append(ex_tunnel)
        # Update config? Maybe user connects first then adds.

    import signal
    
    # 3. Setup Client Core
    client = ControlClient(
        server_host=state.server_host, 
        server_port=state.server_port, 
        identity_key=id_key,
        server_key=server_pub_key_bytes,
    )

    # 4. Setup UI
    window = MainWindow(client, state, cfg_mgr)
    window.show()

    # Handle Ctrl+C
    def signal_handler(sig, frame):
        print("Exiting...")
        app.quit()
        
    signal.signal(signal.SIGINT, signal_handler)

    # 5. Auto-connect
    async def run_client():
        # Trigger connect via UI to reuse logic
        # We manually check the button or call on_connect_toggle?
        # on_connect_toggle toggles state. State is disconnected initially.
        # But we want to 'auto connect' if configured?
        # Let's simple call window.on_connect_toggle() if intended, or just do nothing and let user click?
        # Req: "Auto-connect". 
        
        # Simulate click to "Connect"
        window.btn_connect.click()

    loop.create_task(run_client())
    
    with loop:
        loop.run_forever()

if __name__ == "__main__":
    main()
