import argparse
import asyncio
import logging
import sys

# 이 서버는 "가장 단순한" TCP 에코 서버로,
# 터널 경로에서 데이터가 정상적으로 왕복되는지 확인하기 위한 테스트 용도다.
# test_e2e_process.py 안의 에코 서버 로직을 독립 실행 파일로 분리했다.


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


async def handle_echo(reader: asyncio.StreamReader, writer: asyncio.StreamWriter, buffer_size: int):
    # 연결별 에코 핸들러:
    # 들어온 데이터를 그대로 "ECHO:" 접두어와 함께 다시 돌려준다.
    peer = writer.get_extra_info("peername")
    logging.info("클라이언트 연결: %s", peer)
    try:
        while True:
            data = await reader.read(buffer_size)
            if not data:
                # 연결이 종료된 경우 루프를 빠져나간다.
                logging.info("클라이언트 연결 종료: %s", peer)
                break
            # 수신한 데이터를 그대로 돌려보낸다(접두어 포함).
            writer.write(b"ECHO:" + data)
            await writer.drain()
    except Exception as e:
        # 네트워크 예외는 흔하므로 로그만 남기고 정리한다.
        logging.exception("에코 처리 중 예외: %s", e)
    finally:
        writer.close()
        try:
            await writer.wait_closed()
        except Exception:
            # 닫기 과정에서의 예외는 무시한다.
            pass


async def main():
    parser = argparse.ArgumentParser(description="Simple TCP Echo Server")
    parser.add_argument("--host", default="127.0.0.1", help="Listen host (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=8000, help="Listen port (default: 8000)")
    parser.add_argument("--buffer-size", type=int, default=4096, help="Read buffer size (default: 4096)")
    args = parser.parse_args()

    setup_logging()
    logging.info("에코 서버 시작: %s:%d (buffer=%d)", args.host, args.port, args.buffer_size)

    # asyncio.start_server는 콜백에 reader/writer를 제공하므로 래핑해서 전달한다.
    server = await asyncio.start_server(
        lambda r, w: handle_echo(r, w, args.buffer_size),
        args.host,
        args.port
    )

    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        # Ctrl+C 종료 시 깔끔하게 종료한다.
        logging.info("에코 서버 종료 요청")
