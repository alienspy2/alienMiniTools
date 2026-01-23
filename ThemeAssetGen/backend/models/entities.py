import enum
import uuid
from datetime import datetime

from sqlalchemy import Column, String, DateTime, ForeignKey, Text, Enum
from sqlalchemy.orm import relationship

from .database import Base


class AssetCategory(enum.Enum):
    PROP = "prop"
    FURNITURE = "furniture"
    WALL = "wall"
    CEILING = "ceiling"
    FLOOR = "floor"
    DECORATION = "decoration"
    LIGHTING = "lighting"
    OTHER = "other"


class GenerationStatus(enum.Enum):
    PENDING = "pending"
    GENERATING_2D = "generating_2d"
    GENERATING_3D = "generating_3d"
    COMPLETED = "completed"
    FAILED = "failed"


def generate_uuid():
    return str(uuid.uuid4())


class Theme(Base):
    __tablename__ = "themes"

    id = Column(String(36), primary_key=True, default=generate_uuid)
    name = Column(String(255), nullable=False)
    description = Column(Text)
    created_at = Column(DateTime, default=datetime.utcnow)

    catalogs = relationship("Catalog", back_populates="theme", cascade="all, delete-orphan")


class Catalog(Base):
    __tablename__ = "catalogs"

    id = Column(String(36), primary_key=True, default=generate_uuid)
    name = Column(String(255), nullable=False)
    theme_id = Column(String(36), ForeignKey("themes.id"))
    description = Column(Text)
    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)

    theme = relationship("Theme", back_populates="catalogs")
    assets = relationship("Asset", back_populates="catalog", cascade="all, delete-orphan")


class Asset(Base):
    __tablename__ = "assets"

    id = Column(String(36), primary_key=True, default=generate_uuid)
    catalog_id = Column(String(36), ForeignKey("catalogs.id"))
    name = Column(String(255), nullable=False)
    name_kr = Column(String(255))
    category = Column(Enum(AssetCategory), default=AssetCategory.OTHER)
    description = Column(Text)
    description_kr = Column(Text)
    prompt_2d = Column(Text)

    status = Column(Enum(GenerationStatus), default=GenerationStatus.PENDING)
    error_message = Column(Text)

    preview_image_path = Column(String(512))
    model_glb_path = Column(String(512))
    model_obj_path = Column(String(512))

    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)

    catalog = relationship("Catalog", back_populates="assets")
