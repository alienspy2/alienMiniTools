"""로깅 설정 모듈"""
import os
import logging
import sys
from datetime import datetime
from logging.handlers import RotatingFileHandler
from pathlib import Path

from backend.config import BASE_DIR

# 로그 디렉토리
LOGS_DIR = BASE_DIR / "logs"
LOGS_DIR.mkdir(exist_ok=True)

# UTF-8 환경변수 설정 (콘솔 인코딩)
os.environ["PYTHONIOENCODING"] = "utf-8"


def setup_logging(log_level: str = "INFO") -> None:
    """파일 + 콘솔 로깅 설정

    Args:
        log_level: 로그 레벨 (DEBUG, INFO, WARNING, ERROR)
    """
    level = getattr(logging, log_level.upper(), logging.INFO)

    # 로그 포맷
    detailed_format = logging.Formatter(
        "%(asctime)s %(levelname)-8s [%(name)s:%(lineno)d] %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )
    simple_format = logging.Formatter(
        "%(asctime)s %(levelname)-8s %(message)s",
        datefmt="%H:%M:%S"
    )

    # 루트 로거 설정
    root_logger = logging.getLogger()
    root_logger.setLevel(level)

    # 기존 핸들러 제거
    for handler in root_logger.handlers[:]:
        root_logger.removeHandler(handler)

    # 1. 콘솔 핸들러 (간단한 포맷)
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(level)
    console_handler.setFormatter(simple_format)
    root_logger.addHandler(console_handler)

    # 2. 파일 핸들러 - 일반 로그 (상세 포맷, 10MB 로테이션)
    log_file = LOGS_DIR / f"server_{datetime.now().strftime('%Y%m%d')}.log"
    file_handler = RotatingFileHandler(
        log_file,
        maxBytes=10 * 1024 * 1024,  # 10MB
        backupCount=5,
        encoding="utf-8"
    )
    file_handler.setLevel(level)
    file_handler.setFormatter(detailed_format)
    root_logger.addHandler(file_handler)

    # 3. 에러 전용 파일 핸들러
    error_file = LOGS_DIR / f"error_{datetime.now().strftime('%Y%m%d')}.log"
    error_handler = RotatingFileHandler(
        error_file,
        maxBytes=5 * 1024 * 1024,  # 5MB
        backupCount=3,
        encoding="utf-8"
    )
    error_handler.setLevel(logging.ERROR)
    error_handler.setFormatter(detailed_format)
    root_logger.addHandler(error_handler)

    # uvicorn/httpx 등 외부 라이브러리 로그 레벨 조정
    logging.getLogger("uvicorn").setLevel(logging.INFO)
    logging.getLogger("uvicorn.access").setLevel(logging.WARNING)
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("httpcore").setLevel(logging.WARNING)

    logging.info(f"로깅 설정 완료: level={log_level}, log_dir={LOGS_DIR}")


def get_logger(name: str) -> logging.Logger:
    """모듈별 로거 가져오기"""
    return logging.getLogger(name)
