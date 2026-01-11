from fastapi import APIRouter

from . import theme, asset, catalog, generation

router = APIRouter()

router.include_router(theme.router, prefix="/theme", tags=["theme"])
router.include_router(asset.router, prefix="/asset", tags=["asset"])
router.include_router(catalog.router, prefix="/catalog", tags=["catalog"])
router.include_router(generation.router, prefix="/generation", tags=["generation"])
