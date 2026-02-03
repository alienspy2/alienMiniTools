from typing import Optional, List
from pydantic import BaseModel, Field
from datetime import datetime


# === Request Schemas ===

class ThemeGenerateRequest(BaseModel):
    theme: str = Field(..., description="Theme (e.g., 'Medieval Fantasy Castle', 'Modern Office')")


class AssetEditRequest(BaseModel):
    name: Optional[str] = None
    name_kr: Optional[str] = None
    category: Optional[str] = None
    description: Optional[str] = None
    description_kr: Optional[str] = None
    prompt_2d: Optional[str] = None


class AssetAddRequest(BaseModel):
    name: str
    name_kr: str
    category: str
    description: str
    description_kr: str
    prompt_2d: str


class BatchGenerateRequest(BaseModel):
    asset_ids: Optional[List[str]] = None  # None = generate all


# === Response Schemas ===

class AssetListItem(BaseModel):
    name: str
    name_kr: str
    category: str
    description: str
    description_kr: str
    prompt_2d: str


class AssetResponse(BaseModel):
    id: str
    name: str
    name_kr: Optional[str]
    category: str
    description: Optional[str]
    description_kr: Optional[str]
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


# === Queue Status (Simplified and Robust) ===

class QueueItem(BaseModel):
    """Single item in generation queue"""
    asset_id: str
    asset_name: str
    queue_type: str  # "2d" or "3d"
    status: str  # "pending", "running", "completed", "failed"
    started_at: Optional[str] = None
    error: Optional[str] = None


class QueueStatus(BaseModel):
    """Overall queue status for a catalog"""
    catalog_id: str
    queue_2d: List[QueueItem] = []
    queue_3d: List[QueueItem] = []
    is_running_2d: bool = False
    is_running_3d: bool = False
    current_2d: Optional[QueueItem] = None
    current_3d: Optional[QueueItem] = None


# Legacy - kept for backward compatibility
class BatchGenerationStatus(BaseModel):
    catalog_id: str
    total: int
    completed: int
    failed: int
    current_asset: Optional[str]
    current_status: Optional[str]
    current_index: int = 0
    pending_assets: List[str] = []
    started_at: Optional[str] = None
