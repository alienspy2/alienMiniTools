from enum import IntEnum

PROTOCOL_VERSION = 1
DEFAULT_CONTROL_PORT = 9000
FRAME_HEAD_LEN = 4  # Length (u32)
FRAME_NONCE_LEN = 12
TAG_LEN = 16

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
