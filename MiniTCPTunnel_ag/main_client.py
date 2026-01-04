import sys
import argparse
import logging
import asyncio
from PySide6.QtWidgets import QApplication
import qasync

def main():
    parser = argparse.ArgumentParser(description="MiniTCPTunnel Client")
    parser.add_argument("--config", type=str, default="client.json", help="Path to configuration file")
    args = parser.parse_args()

    logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
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
    
    # Populate Tunnels
    for t_def in cfg_mgr.config.tunnels:
        vm = TunnelViewModel(t_def.id, t_def.remote_port, t_def.local_host, t_def.local_port)
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
        identity_key=id_key
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
