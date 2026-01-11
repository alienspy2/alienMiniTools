from typing import Optional, List
from pydantic import BaseModel, Field
from datetime import datetime


# === 요청 스키마 ===

class ThemeGenerateRequest(BaseModel):
    theme: str = Field(..., description="테마 (예: '중세 판타지 성', '현대 오피스')")


class AssetEditRequest(BaseModel):
    name: Optional[str] = None
    name_kr: Optional[str] = None
    category: Optional[str] = None
    description: Optional[str] = None
    prompt_2d: Optional[str] = None


class AssetAddRequest(BaseModel):
    name: str
    name_kr: str
    category: str
    description: str
    prompt_2d: str


class BatchGenerateRequest(BaseModel):
    asset_ids: Optional[List[str]] = None  # None이면 전체 생성


# === 응답 스키마 ===

class AssetListItem(BaseModel):
    name: str
    name_kr: str
    category: str
    description: str
    prompt_2d: str


class AssetResponse(BaseModel):
    id: str
    name: str
    name_kr: Optional[str]
    category: str
    description: Optional[str]
    prompt_2d: Optional[str]
    status: str
    error_message: Optional[str]
    preview_url: Optional[str]
    model_glb_url: Optional[str]
    model_obj_url: Optional[str]
    created_at: datetime
    updated_at: Optional[datetime]

    class Config:
        from_attributes = True


class CatalogResponse(BaseModel):
    id: str
    name: str
    theme_id: str
    theme_name: str
    description: Optional[str]
    asset_count: int
    completed_count: int
    assets: List[AssetResponse]
    created_at: datetime
    updated_at: Optional[datetime]

    class Config:
        from_attributes = True


class CatalogListItem(BaseModel):
    id: str
    name: str
    theme_name: str
    asset_count: int
    completed_count: int
    created_at: datetime

    class Config:
        from_attributes = True


class CatalogListResponse(BaseModel):
    catalogs: List[CatalogListItem]


class ThemeGenerateResponse(BaseModel):
    theme_id: str
    catalog_id: str
    theme_name: str
    assets: List[AssetListItem]


class GenerationStatusResponse(BaseModel):
    asset_id: str
    status: str
    progress: int  # 0-100
    message: str


class BatchGenerationStatus(BaseModel):
    catalog_id: str
    total: int
    completed: int
    failed: int
    current_asset: Optional[str]
    current_status: Optional[str]
