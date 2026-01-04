import argparse
import logging
import socket
import sys
import time

# 이 클라이언트는 simple_echo_server.py 로 데이터를 보내고,
# 동일한 데이터가 "ECHO:" 접두어와 함께 돌아오는지 확인한다.


def setup_logging():
    # Windows 기본 인코딩(cp949)로 출력되면 로그가 깨질 수 있으므로,
    # stdout/stderr를 UTF-8로 재설정해 리다이렉션 파일도 UTF-8로 저장되게 한다.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except Exception:
        # 일부 환경에서는 reconfigure가 없을 수 있으니 안전하게 무시한다.
        pass

    # 로그를 stdout 으로 보내서 `> file.log` 리다이렉션이 쉽게 되도록 한다.
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s",
        stream=sys.stdout
    )


def recv_exact(sock: socket.socket, expected_len: int, chunk_size: int) -> bytes:
    # 필요한 길이만큼 읽어올 때까지 루프를 돌며 수신한다.
    data = bytearray()
    while len(data) < expected_len:
        chunk = sock.recv(chunk_size)
        if not chunk:
            break
        data.extend(chunk)
    return bytes(data)


def run_once(host: str, port: int, message: bytes, expect_prefix: bytes, timeout: float, chunk_size: int) -> bool:
    # 1) 서버에 TCP 연결
    with socket.create_connection((host, port), timeout=timeout) as sock:
        sock.settimeout(timeout)
        logging.info("서버 연결 완료: %s:%d", host, port)

        # 2) 데이터 전송
        sock.sendall(message)
        logging.info("데이터 전송 완료: %d bytes", len(message))

        # 3) 응답 수신 (prefix + 원본 메시지 길이만큼)
        expected = expect_prefix + message
        response = recv_exact(sock, len(expected), chunk_size)
        logging.info("응답 수신 완료: %d bytes", len(response))

        print(response)

        # 4) 응답 검증
        if response == expected:
            logging.info("응답 검증 성공")
            return True
        logging.error("응답 검증 실패: expected=%r actual=%r", expected, response)

        return False


def main():
    parser = argparse.ArgumentParser(description="Simple TCP Echo Client")
    parser.add_argument("--host", default="127.0.0.1", help="Server host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8000, help="Server port (default: 8000)")
    parser.add_argument("--message", default="PING", help="Message to send")
    parser.add_argument("--timeout", type=float, default=5.0, help="Socket timeout seconds")
    parser.add_argument("--repeat", type=int, default=1, help="Repeat count")
    parser.add_argument("--sleep", type=float, default=0.5, help="Sleep seconds between repeats")
    parser.add_argument("--chunk-size", type=int, default=4096, help="Receive chunk size")
    parser.add_argument("--expect-prefix", default="ECHO:", help="Expected response prefix")
    args = parser.parse_args()

    setup_logging()

    message_bytes = args.message.encode("utf-8")
    prefix_bytes = args.expect_prefix.encode("utf-8")

    logging.info("에코 클라이언트 시작: %s:%d", args.host, args.port)
    logging.info("전송 메시지: %r", message_bytes)

    success = True
    for i in range(args.repeat):
        logging.info("테스트 시도 %d/%d", i + 1, args.repeat)
        try:
            ok = run_once(
                host=args.host,
                port=args.port,
                message=message_bytes,
                expect_prefix=prefix_bytes,
                timeout=args.timeout,
                chunk_size=args.chunk_size
            )
            success = success and ok
        except Exception as e:
            logging.exception("테스트 중 예외 발생: %s", e)
            success = False

        if i + 1 < args.repeat:
            time.sleep(args.sleep)

    if not success:
        sys.exit(1)


if __name__ == "__main__":
    main()
