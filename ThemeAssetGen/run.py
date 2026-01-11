#!/usr/bin/env python
"""ThemeAssetGen 서버 실행 스크립트"""

import argparse
import sys
from pathlib import Path

# 프로젝트 루트를 Python 경로에 추가
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

import uvicorn
from backend.config import SERVER_HOST, SERVER_PORT
from backend.logging_config import setup_logging, LOGS_DIR


def main():
    parser = argparse.ArgumentParser(description="ThemeAssetGen 서버")
    parser.add_argument("--debug", action="store_true", help="디버그 모드 활성화")
    parser.add_argument("--log-level", default="INFO", choices=["DEBUG", "INFO", "WARNING", "ERROR"])
    args = parser.parse_args()

    # 로깅 설정
    log_level = "DEBUG" if args.debug else args.log_level
    setup_logging(log_level)

    print("=" * 50)
    print("  ThemeAssetGen 서버 시작")
    print("=" * 50)
    print(f"  URL: http://{SERVER_HOST}:{SERVER_PORT}")
    print(f"  API 문서: http://localhost:{SERVER_PORT}/docs")
    print(f"  로그 디렉토리: {LOGS_DIR}")
    print(f"  로그 레벨: {log_level}")
    print("=" * 50)

    uvicorn.run(
        "backend.main:app",
        host=SERVER_HOST,
        port=SERVER_PORT,
        reload=not args.debug,  # 디버그 모드에서는 reload 비활성화 (더 나은 에러 추적)
        log_level=log_level.lower(),
    )


if __name__ == "__main__":
    main()
