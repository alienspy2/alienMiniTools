#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""배치 생성 테스트 스크립트 - 디버그 모드"""

import os
import asyncio
import sys
from pathlib import Path

# UTF-8 모드 강제 설정
os.environ["PYTHONIOENCODING"] = "utf-8"

# 프로젝트 루트를 Python 경로에 추가
project_root = Path(__file__).parent
sys.path.insert(0, str(project_root))

from backend.logging_config import setup_logging
from backend.models.database import SessionLocal, init_db
from backend.models.entities import Catalog, Asset, GenerationStatus


def list_catalogs():
    """카탈로그 목록 출력"""
    db = SessionLocal()
    try:
        catalogs = db.query(Catalog).all()
        print("\n=== 카탈로그 목록 ===")
        for i, catalog in enumerate(catalogs):
            asset_count = len(catalog.assets)
            pending = len([a for a in catalog.assets if a.status == GenerationStatus.PENDING])
            completed = len([a for a in catalog.assets if a.status == GenerationStatus.COMPLETED])
            failed = len([a for a in catalog.assets if a.status == GenerationStatus.FAILED])
            print(f"[{i+1}] {catalog.name}")
            print(f"    ID: {catalog.id}")
            print(f"    에셋: {asset_count}개 (대기: {pending}, 완료: {completed}, 실패: {failed})")
        print()
        return catalogs
    finally:
        db.close()


def list_assets(catalog_id: str):
    """카탈로그의 에셋 목록 출력"""
    db = SessionLocal()
    try:
        assets = db.query(Asset).filter(Asset.catalog_id == catalog_id).all()
        print(f"\n=== 에셋 목록 ({len(assets)}개) ===")
        for i, asset in enumerate(assets):
            status_emoji = {
                GenerationStatus.PENDING: "⏳",
                GenerationStatus.GENERATING_2D: "🎨",
                GenerationStatus.GENERATING_3D: "🎮",
                GenerationStatus.COMPLETED: "✅",
                GenerationStatus.FAILED: "❌",
            }.get(asset.status, "?")
            print(f"[{i+1}] {status_emoji} {asset.name_kr or asset.name} [{asset.status.value}]")
            if asset.error_message:
                print(f"    에러: {asset.error_message}")
        print()
        return assets
    finally:
        db.close()


async def test_single_asset(asset_id: str):
    """단일 에셋 생성 테스트"""
    import logging
    logger = logging.getLogger(__name__)

    db = SessionLocal()
    try:
        from backend.services.pipeline_service import PipelineService

        asset = db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            print(f"에셋을 찾을 수 없음: {asset_id}")
            return

        print(f"\n=== 단일 에셋 생성 시작 ===")
        print(f"에셋: {asset.name_kr or asset.name}")
        print(f"ID: {asset.id}")
        print(f"2D 프롬프트: {asset.prompt_2d[:100]}...")
        print()

        pipeline = PipelineService(db)

        logger.info(f"에셋 생성 시작: {asset.name}")
        await pipeline.generate_single_asset(asset_id)
        logger.info(f"에셋 생성 완료: {asset.name}")

        # 결과 확인
        db.refresh(asset)
        print(f"\n=== 결과 ===")
        print(f"상태: {asset.status.value}")
        print(f"2D 이미지: {asset.preview_image_path}")
        print(f"3D GLB: {asset.model_glb_path}")
        print(f"3D OBJ: {asset.model_obj_path}")
        if asset.error_message:
            print(f"에러: {asset.error_message}")

    except Exception as e:
        logger.exception(f"에셋 생성 실패: {e}")
    finally:
        db.close()


async def test_batch(catalog_id: str, limit: int = None):
    """배치 생성 테스트"""
    import logging
    logger = logging.getLogger(__name__)

    db = SessionLocal()
    try:
        from backend.services.pipeline_service import PipelineService

        catalog = db.query(Catalog).filter(Catalog.id == catalog_id).first()
        if not catalog:
            print(f"카탈로그를 찾을 수 없음: {catalog_id}")
            return

        # 대기 중인 에셋만 선택
        assets = db.query(Asset).filter(
            Asset.catalog_id == catalog_id,
            Asset.status == GenerationStatus.PENDING
        ).all()

        if limit:
            assets = assets[:limit]

        print(f"\n=== 배치 생성 시작 ===")
        print(f"카탈로그: {catalog.name}")
        print(f"생성할 에셋: {len(assets)}개")
        print()

        asset_ids = [a.id for a in assets]

        pipeline = PipelineService(db)
        await pipeline.generate_batch(catalog_id, asset_ids)

        print(f"\n=== 배치 생성 완료 ===")

    except Exception as e:
        logger.exception(f"배치 생성 실패: {e}")
    finally:
        db.close()


async def check_services():
    """서비스 상태 확인"""
    print("\n=== 서비스 상태 확인 ===")

    # ComfyUI
    from backend.services.comfyui_service import ComfyUIService
    comfyui = ComfyUIService()
    try:
        comfyui_ok = await comfyui.check_health()
        print(f"ComfyUI: {'✅ OK' if comfyui_ok else '❌ FAIL'} ({comfyui.base_url})")
    except Exception as e:
        print(f"ComfyUI: ❌ FAIL - {e}")

    # Hunyuan3D
    from backend.services.hunyuan2_service import Hunyuan3DService
    hunyuan = Hunyuan3DService()
    try:
        hunyuan_ok = await hunyuan.check_health()
        print(f"Hunyuan3D: {'✅ OK' if hunyuan_ok else '❌ FAIL'} ({hunyuan.base_url})")
    except Exception as e:
        print(f"Hunyuan3D: ❌ FAIL - {e}")

    # Ollama
    from backend.services.ollama_service import OllamaService
    ollama = OllamaService()
    try:
        ollama_ok = await ollama.check_health()
        print(f"Ollama: {'✅ OK' if ollama_ok else '❌ FAIL'} ({ollama.base_url})")
    except Exception as e:
        print(f"Ollama: ❌ FAIL - {e}")

    print()


def main():
    import argparse
    parser = argparse.ArgumentParser(description="ThemeAssetGen 배치 테스트")
    parser.add_argument("--list", action="store_true", help="카탈로그 목록")
    parser.add_argument("--assets", type=str, help="에셋 목록 (카탈로그 ID)")
    parser.add_argument("--check", action="store_true", help="서비스 상태 확인")
    parser.add_argument("--single", type=str, help="단일 에셋 생성 (에셋 ID)")
    parser.add_argument("--batch", type=str, help="배치 생성 (카탈로그 ID)")
    parser.add_argument("--limit", type=int, default=1, help="배치 생성 시 최대 개수 (기본: 1)")
    args = parser.parse_args()

    # 로깅 설정 (DEBUG 레벨)
    setup_logging("DEBUG")

    # DB 초기화
    init_db()

    if args.list:
        list_catalogs()
    elif args.assets:
        list_assets(args.assets)
    elif args.check:
        asyncio.run(check_services())
    elif args.single:
        asyncio.run(test_single_asset(args.single))
    elif args.batch:
        asyncio.run(test_batch(args.batch, args.limit))
    else:
        # 기본: 서비스 체크 + 카탈로그 목록
        asyncio.run(check_services())
        list_catalogs()
        print("사용법:")
        print("  python test_batch.py --check          # 서비스 상태 확인")
        print("  python test_batch.py --list           # 카탈로그 목록")
        print("  python test_batch.py --assets <id>    # 에셋 목록")
        print("  python test_batch.py --single <id>    # 단일 에셋 생성")
        print("  python test_batch.py --batch <id>     # 배치 생성")
        print("  python test_batch.py --batch <id> --limit 3  # 3개만 생성")


if __name__ == "__main__":
    main()
