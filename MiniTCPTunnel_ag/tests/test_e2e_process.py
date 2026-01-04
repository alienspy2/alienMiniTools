import asyncio
import json
import logging
import subprocess
import sys
import socket
from pathlib import Path

import nacl.encoding
import nacl.signing
# 테스트 실행 위치와 무관하게 로컬 패키지를 import할 수 있도록
# 레포지토리 루트를 sys.path에 추가한다.
ROOT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT_DIR))

from mini_tcp_tunnel.client.core import ControlClient, TunnelConfig

# 테스트 전용 설정값(선호 포트)
SERVER_PORT = 9010
ECHO_PORT = 9980
TUNNEL_PORT = 9999
PYTHON_EXE = sys.executable

# 테스트가 수정하는 파일 경로
CLIENT_CONFIG_PATH = Path("client_config.json")
ALLOWED_CLIENTS_PATH = Path("allowed_clients.txt")
SERVER_KEY_PATH = Path("server_identity_key.hex")

# Windows 기본 인코딩(cp949)으로 출력되면 로그가 깨질 수 있으므로
# stdout/stderr를 UTF-8로 강제해 리다이렉션 파일도 UTF-8로 저장되게 한다.
try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    # 일부 환경에서는 reconfigure가 없을 수 있으므로 무시한다.
    pass

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", stream=sys.stdout)

def backup_files(paths):
    """
    테스트로 인해 변경되는 파일을 백업한다.
    파일이 없던 경우는 None으로 기록해 종료 시 원상복구한다.
    """
    backups = {}
    for path in paths:
        if path.exists():
            backups[path] = path.read_bytes()
        else:
            backups[path] = None
    return backups

def restore_files(backups):
    """
    테스트 종료 시 파일을 원래 상태로 복구한다.
    테스트가 만든 파일은 삭제하고, 기존 파일은 원래 내용으로 되돌린다.
    """
    for path, content in backups.items():
        if content is None:
            if path.exists():
                path.unlink()
        else:
            path.write_bytes(content)

def generate_identity_pair():
    """
    Ed25519 키를 생성하고, 개인키/공개키를 hex 문자열로 반환한다.
    테스트에서 서버/클라이언트 키를 미리 생성해 파일에 고정한다.
    """
    signing_key = nacl.signing.SigningKey.generate()
    private_hex = signing_key.encode(encoder=nacl.encoding.HexEncoder).decode("utf-8")
    public_hex = signing_key.verify_key.encode(encoder=nacl.encoding.HexEncoder).decode("utf-8")
    return private_hex, public_hex

def write_test_files(server_private_hex, server_public_hex, client_private_hex, client_public_hex, server_port, tunnel_port, echo_port):
    """
    테스트용 설정 파일을 생성한다.
    - 서버 키는 server_identity_key.hex에 고정 저장한다.
    - allowed_clients.txt에는 클라이언트 공개키를 등록한다.
    - client_config.json에는 server_pub_key와 클라이언트 개인키를 반영한다.
    """
    SERVER_KEY_PATH.write_bytes(server_private_hex.encode("utf-8"))
    ALLOWED_CLIENTS_PATH.write_bytes(
        (
            "# Add Client Ed25519 Public Keys here (Hex format), one per line\n"
            f"{client_public_hex}\n"
        ).encode("utf-8")
    )
    client_config = {
        "server_host": "127.0.0.1",
        "server_port": server_port,
        "server_pub_key": server_public_hex,
        "identity_private_key_hex": client_private_hex,
        "tunnels": [
            {
                "id": "e2e-echo",
                "remote_port": tunnel_port,
                "local_host": "127.0.0.1",
                "local_port": echo_port,
                "auto_start": True,
            }
        ],
    }
    CLIENT_CONFIG_PATH.write_bytes(json.dumps(client_config, indent=2).encode("utf-8"))

def find_free_port(preferred_port=None):
    """
    사용 가능한 포트를 찾는다.
    선호 포트가 사용 중이면 OS가 할당한 임의 포트를 사용한다.
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        if preferred_port:
            try:
                sock.bind(("127.0.0.1", preferred_port))
            except OSError:
                sock.close()
                sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                sock.bind(("127.0.0.1", 0))
        else:
            sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    finally:
        sock.close()
    return port

async def start_echo_server(port):
    server = await asyncio.start_server(handle_echo, "127.0.0.1", port)
    logging.info(f"Echo Server listening on {port}")
    return server

async def handle_echo(reader, writer):
    try:
        while True:
            data = await reader.read(100)
            if not data:
                break
            writer.write(b"ECHO:" + data)
            await writer.drain()
    except Exception:
        pass
    finally:
        writer.close()

def run_process(cmd):
    # subprocess 출력은 상위 프로세스(stdout/stderr)로 전달해 로그를 한 곳에 모은다.
    return subprocess.Popen(cmd, stdout=sys.stdout, stderr=sys.stderr)

def terminate_process(proc, name):
    """
    프로세스를 안전하게 종료한다.
    종료가 지연되면 강제 종료로 전환한다.
    """
    if not proc:
        return
    if proc.poll() is not None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except Exception:
        logging.warning(f"{name} 강제 종료")
        proc.kill()

async def wait_until(predicate, timeout_sec, interval_sec=0.2):
    """
    조건이 만족될 때까지 대기한다.
    지정된 시간 내에 조건이 성립하지 않으면 False를 반환한다.
    """
    start = asyncio.get_event_loop().time()
    while True:
        if predicate():
            return True
        if (asyncio.get_event_loop().time() - start) > timeout_sec:
            return False
        await asyncio.sleep(interval_sec)

async def wait_for_connected(client, timeout_sec=10):
    """
    ControlClient가 연결 상태로 전환될 때까지 대기한다.
    """
    ok = await wait_until(lambda: client.is_connected, timeout_sec)
    if not ok:
        raise TimeoutError("클라이언트 연결 대기 시간 초과")

async def wait_for_hmac_key(client, timeout_sec=10):
    """
    서버에서 AUTH_OK로 전달한 HMAC 세션 키 수신을 대기한다.
    """
    ok = await wait_until(lambda: client.session_hmac_key is not None, timeout_sec)
    if not ok:
        raise TimeoutError("HMAC 세션 키 수신 대기 시간 초과")

async def wait_for_tunnel_open(client, tunnel_id, timeout_sec=10):
    """
    서버에서 터널이 열렸다고 통지될 때까지 대기한다.
    """
    ok = await wait_until(lambda: tunnel_id in client.running_tunnels, timeout_sec)
    if not ok:
        raise TimeoutError("터널 오픈 상태 대기 시간 초과")

async def test_tunnel_connection(tunnel_port):
    """
    터널 포트로 접속해 에코 응답이 정상인지 확인한다.
    핸드셰이크/설정 적용 지연을 고려해 재시도를 수행한다.
    """
    msg = b"INTEGRATION_TEST"
    for attempt in range(1, 6):
        try:
            reader, writer = await asyncio.open_connection("127.0.0.1", tunnel_port)
            writer.write(msg)
            await writer.drain()
            data = await asyncio.wait_for(reader.read(1024), timeout=5)
            logging.info(f"Received: {data}")
            writer.close()
            await writer.wait_closed()
            if data == b"ECHO:" + msg:
                logging.info(">>> SUCCESS: Tunnel works correctly! <<<")
                return True
            logging.error(">>> FAIL: Echo mismatch")
        except Exception as e:
            logging.error(f">>> FAIL: Connection error (attempt {attempt}): {e}")
        await asyncio.sleep(1)
    return False

async def main():
    # 테스트 파일 백업(기존 환경에 영향 없도록 복원)
    backups = backup_files([CLIENT_CONFIG_PATH, ALLOWED_CLIENTS_PATH, SERVER_KEY_PATH])
    echo_server = None
    p_server = None
    client = None
    success = False

    try:
        # 1) 서버/클라이언트 키를 생성하고 테스트용 설정을 파일에 반영한다.
        server_private_hex, server_public_hex = generate_identity_pair()
        client_private_hex, client_public_hex = generate_identity_pair()
        # 포트 충돌을 방지하기 위해 OS가 할당한 사용 가능 포트를 사용한다.
        server_port = find_free_port()
        echo_port = find_free_port()
        tunnel_port = find_free_port()
        # 동일 포트가 중복 선택되면 다시 뽑는다.
        used_ports = {server_port}
        while echo_port in used_ports:
            echo_port = find_free_port()
        used_ports.add(echo_port)
        while tunnel_port in used_ports:
            tunnel_port = find_free_port()
        logging.info(f"Selected ports - server={server_port}, echo={echo_port}, tunnel={tunnel_port}")
        write_test_files(
            server_private_hex,
            server_public_hex,
            client_private_hex,
            client_public_hex,
            server_port,
            tunnel_port,
            echo_port,
        )

        # 2) 로컬 에코 서버 시작
        echo_server = await start_echo_server(echo_port)

        # 3) 터널 서버 시작 (키 고정 및 허용 클라이언트 등록 포함)
        logging.info("Starting Tunnel Server...")
        # 테스트가 길어질 수 있어 timeout을 넉넉히 잡는다.
        p_server = run_process([PYTHON_EXE, "main_server.py", "--port", str(server_port), "--timeout", "30"])
        await asyncio.sleep(2)

        # 4) 헤드리스 클라이언트 생성 및 연결
        logging.info("Starting Tunnel Client (headless)...")
        client_identity = nacl.signing.SigningKey(bytes.fromhex(client_private_hex))
        server_key_bytes = bytes.fromhex(server_public_hex)
        client = ControlClient(
            server_host="127.0.0.1",
            server_port=server_port,
            identity_key=client_identity,
            server_key=server_key_bytes,
        )
        tunnel_cfg = TunnelConfig("e2e-echo", tunnel_port, "127.0.0.1", echo_port, enabled=True)
        client.add_tunnel(tunnel_cfg)
        await client.connect()
        await wait_for_connected(client, timeout_sec=10)
        await wait_for_hmac_key(client, timeout_sec=10)
        await wait_for_tunnel_open(client, tunnel_cfg.tid, timeout_sec=10)

        # 5) 터널 통신 테스트
        logging.info("Testing Tunnel Connection...")
        success = await test_tunnel_connection(tunnel_port)

    finally:
        # 6) 종료 및 복구
        logging.info("Shutting down processes...")
        if client:
            await client.disconnect()
        terminate_process(p_server, "server")
        if echo_server:
            echo_server.close()
            await echo_server.wait_closed()
        restore_files(backups)

    if not success:
        sys.exit(1)

if __name__ == "__main__":
    asyncio.run(main())
