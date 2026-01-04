from datetime import datetime

class TunnelViewModel:
    def __init__(self, tid, remote_port, local_host, local_port, enabled: bool = True):
        self.tid = tid
        self.remote_port = remote_port
        self.local_host = local_host
        self.local_port = local_port
        self.status = "Stopped"
        self.connections = 0
        self.enabled = enabled

class LogModel:
    def __init__(self, msg, level="INFO"):
        self.timestamp = datetime.now().strftime("%H:%M:%S")
        self.msg = msg
        self.level = level

class AppState:
    tunnels: list[TunnelViewModel] = []
    logs: list[LogModel] = []
    
    server_host = "127.0.0.1"
    server_port = 9000
    server_pub_key = ""
