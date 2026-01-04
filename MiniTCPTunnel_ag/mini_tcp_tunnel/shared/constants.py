from enum import IntEnum

PROTOCOL_VERSION = 1
DEFAULT_CONTROL_PORT = 9000
FRAME_HEAD_LEN = 4  # Length (u32)
FRAME_NONCE_LEN = 12
TAG_LEN = 16

# --- 보안 관련 상한값 ---
# 핸드셰이크 헬로 메시지는 고정 길이(143 bytes)로 설계되어 있다.
# 길이를 엄격히 제한해 비정상/과대 길이로 인한 메모리/시간 소모를 차단한다.
HANDSHAKE_HELLO_LEN = 143
MAX_HANDSHAKE_LEN = 256

# 프레임 크기 상한(압축된 암호문 길이 기준)
# 과도한 길이는 메모리/CPU 소모를 유발하므로 제한한다.
MAX_FRAME_LEN = 4 * 1024 * 1024  # 4 MiB

# 복호화+압축해제 후 평문 길이 상한
# 압축 폭탄을 완전히 막을 수는 없지만, 과도한 확장을 탐지해 종료한다.
MAX_PLAINTEXT_LEN = 8 * 1024 * 1024  # 8 MiB

# 데이터 채널 바인딩용 HMAC 키/토큰 길이 (SHA-256)
HMAC_KEY_LEN = 32
HMAC_TOKEN_LEN = 32
# 데이터 채널 conn_id 길이 상한(DoS 방어 및 파싱 안전성 확보)
MAX_CONN_ID_LEN = 64

class MsgType(IntEnum):
    HELLO = 1
    AUTH_OK = 2
    AUTH_FAIL = 3
    OPEN_TUNNEL = 10
    CLOSE_TUNNEL = 11
    TUNNEL_STATUS = 12
    INCOMING_CONN = 20
    DATA_CONN_READY = 21
    DATA = 30
    HEARTBEAT = 90
    ERROR = 99

class Role(IntEnum):
    SERVER = 0
    CLIENT = 1
