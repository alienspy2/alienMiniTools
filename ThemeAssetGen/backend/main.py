import logging
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse, HTMLResponse

from backend.config import BASE_DIR, CATALOGS_DIR
from backend.models.database import init_db
from backend.api.routes import router as api_router
from backend.logging_config import get_logger

logger = get_logger(__name__)

# FastAPI 앱 생성
app = FastAPI(
    title="ThemeAssetGen",
    description="테마 기반 3D 에셋 자동 생성 API",
    version="1.0.0",
)

# CORS 설정
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# API 라우터 등록
app.include_router(api_router, prefix="/api")

# 정적 파일 서빙 (프론트엔드)
frontend_dir = BASE_DIR / "frontend"
if frontend_dir.exists():
    app.mount("/static", StaticFiles(directory=str(frontend_dir)), name="static")


@app.on_event("startup")
async def startup_event():
    """서버 시작 시 초기화"""
    logger.info("ThemeAssetGen 서버 시작")
    init_db()
    logger.info("데이터베이스 초기화 완료")


@app.get("/", response_class=HTMLResponse)
async def root():
    """메인 페이지"""
    index_path = frontend_dir / "index.html"
    if index_path.exists():
        return FileResponse(index_path)
    return HTMLResponse("<h1>ThemeAssetGen</h1><p>Frontend not found</p>")


@app.get("/files/preview/{asset_id}")
async def get_preview_image(asset_id: str):
    """2D 프리뷰 이미지 서빙"""
    from backend.models.database import SessionLocal
    from backend.models.entities import Asset

    db = SessionLocal()
    try:
        asset = db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset or not asset.preview_image_path:
            raise HTTPException(status_code=404, detail="Preview not found")

        path = Path(asset.preview_image_path)
        if not path.exists():
            raise HTTPException(status_code=404, detail="File not found")

        return FileResponse(path, media_type="image/png")
    finally:
        db.close()


@app.get("/files/model/{asset_id}/{format}")
async def get_model_file(asset_id: str, format: str):
    """3D 모델 파일 서빙"""
    from backend.models.database import SessionLocal
    from backend.models.entities import Asset

    db = SessionLocal()
    try:
        asset = db.query(Asset).filter(Asset.id == asset_id).first()
        if not asset:
            raise HTTPException(status_code=404, detail="Asset not found")

        if format == "glb":
            file_path = asset.model_glb_path
            media_type = "model/gltf-binary"
        elif format == "obj":
            file_path = asset.model_obj_path
            media_type = "text/plain"
        else:
            raise HTTPException(status_code=400, detail="Invalid format")

        if not file_path:
            raise HTTPException(status_code=404, detail="Model not found")

        path = Path(file_path)
        if not path.exists():
            raise HTTPException(status_code=404, detail="File not found")

        return FileResponse(
            path,
            media_type=media_type,
            filename=f"{asset.name}.{format}"
        )
    finally:
        db.close()


@app.get("/health")
async def health_check():
    """헬스 체크"""
    return {"status": "ok"}
