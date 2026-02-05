# Import generation counts from config
from backend.config import ASSET_GENERATION_COUNTS

# Asset category definitions with counts from config
ASSET_CATEGORIES = {
    # Background assets
    "wall_texture": {
        "name": "Wall Texture",
        "name_kr": "Wall Texture",
        "count": ASSET_GENERATION_COUNTS.get("wall_texture", 10),
        "category": "wall",
        "description": "tileable wall texture panels for game environments"
    },
    "stair": {
        "name": "Stair",
        "name_kr": "Stair",
        "count": ASSET_GENERATION_COUNTS.get("stair", 3),
        "category": "floor",
        "description": "stairs of different heights (low, medium, high)"
    },
    "floor_texture": {
        "name": "Floor Texture",
        "name_kr": "Floor Texture",
        "count": ASSET_GENERATION_COUNTS.get("floor_texture", 10),
        "category": "floor",
        "description": "tileable floor texture panels for game environments"
    },
    "door": {
        "name": "Door",
        "name_kr": "Door",
        "count": ASSET_GENERATION_COUNTS.get("door", 5),
        "category": "wall",
        "description": "various door styles for game environments"
    },
    # Props
    "prop_small": {
        "name": "Small Prop",
        "name_kr": "Small Prop",
        "count": ASSET_GENERATION_COUNTS.get("prop_small", 10),
        "category": "prop",
        "description": "small decorative objects (books, cups, bottles, etc.)"
    },
    "prop_medium": {
        "name": "Medium Prop",
        "name_kr": "Medium Prop",
        "count": ASSET_GENERATION_COUNTS.get("prop_medium", 10),
        "category": "prop",
        "description": "medium-sized objects (chairs, baskets, boxes, etc.)"
    },
    "prop_large": {
        "name": "Large Prop",
        "name_kr": "Large Prop",
        "count": ASSET_GENERATION_COUNTS.get("prop_large", 10),
        "category": "furniture",
        "description": "large furniture and objects (tables, wardrobes, statues, etc.)"
    },
}


ASSET_LIST_PROMPT = """You are a professional 3D environment artist.
Generate an asset list for building a 3D virtual space with '{theme}' theme.

## Requirements
1. Generate exactly 10-20 assets.
2. Include various categories.
3. Write 2D image generation prompts for each asset.
4. Prompts must be in English, optimized for 3D asset generation.

## Categories
- prop: Props (books, bottles, plates, etc.)
- furniture: Furniture (chairs, tables, beds, etc.)
- wall: Wall-related (walls, doors, window frames, etc.)
- ceiling: Ceiling (light fixtures, decorations, etc.)
- floor: Floor (tiles, carpets, rugs, etc.)
- decoration: Decorations (paintings, plants, statues, etc.)
- lighting: Lighting (lamps, chandeliers, etc.)

## Output Format (JSON)
```json
{{
  "assets": [
    {{
      "name": "english_name (snake_case)",
      "name_kr": "Korean name",
      "category": "category",
      "description": "Brief English description (1-2 sentences)",
      "description_kr": "Brief Korean description (1-2 sentences)",
      "prompt_2d": "English prompt for 2D image generation (detailed, no background, single object, studio lighting)"
    }}
  ]
}}
```

## Prompt Rules (IMPORTANT!)
- MUST include "isolated on pure white background" or "on solid white background"
- Include "single object, centered"
- Include "full object visible, not cropped, entire object in frame"
- Include "no background, no shadow, no floor"
- Include "product photography style, studio lighting"

Generate the asset list for '{theme}' theme in JSON format now.
Output ONLY valid JSON. Do not include other explanations."""


CATEGORY_ASSET_PROMPT = """You are a professional 3D environment artist.
Generate {count} assets of type "{asset_type}" for a '{theme}' themed 3D virtual space.

## Asset Type Description
{asset_type_description}

## Existing Assets (AVOID DUPLICATES)
{existing_assets}

## Requirements
1. Generate EXACTLY {count} assets of the specified type.
2. Each must be unique and fit the theme.
3. Write detailed 2D image generation prompts.
4. Prompts must be in English, optimized for 3D asset generation.

## Output Format (JSON)
```json
{{
  "assets": [
    {{
      "name": "english_name (snake_case)",
      "name_kr": "Korean name",
      "category": "{category}",
      "asset_type": "{asset_type}",
      "description": "Brief English description (1-2 sentences)",
      "description_kr": "Brief Korean description (1-2 sentences)",
      "prompt_2d": "English prompt for 2D image generation"
    }}
  ]
}}
```

## Prompt Rules (IMPORTANT!)
- MUST include "isolated on pure white background" or "on solid white background"
- Include "single object, centered"
- Include "full object visible, not cropped, entire object in frame"
- Include "no background, no shadow, no floor"
- Include "product photography style, studio lighting"
- Be specific about the style matching '{theme}' theme

Generate EXACTLY {count} "{asset_type}" assets for '{theme}' theme in JSON format now.
Output ONLY valid JSON. Do not include other explanations."""


ADDITIONAL_ASSETS_PROMPT = """You are a professional 3D environment artist.
Generate additional assets for a '{theme}' themed 3D virtual space.

## Existing Assets (AVOID DUPLICATES)
{existing_assets}

## Requirements
1. Generate EXACTLY {count} new assets.
2. Avoid duplicating existing assets.
3. Include various categories.
4. Write detailed 2D image generation prompts.
5. Prompts must be in English, optimized for 3D asset generation.

## Categories
- prop: Props (books, bottles, plates, etc.)
- furniture: Furniture (chairs, tables, beds, etc.)
- wall: Wall-related (walls, doors, window frames, etc.)
- ceiling: Ceiling (light fixtures, decorations, etc.)
- floor: Floor (tiles, carpets, rugs, etc.)
- decoration: Decorations (paintings, plants, statues, etc.)
- lighting: Lighting (lamps, chandeliers, etc.)

## Output Format (JSON)
```json
{{
  "assets": [
    {{
      "name": "english_name (snake_case)",
      "name_kr": "Korean name",
      "category": "category",
      "description": "Brief English description (1-2 sentences)",
      "description_kr": "Brief Korean description (1-2 sentences)",
      "prompt_2d": "English prompt for 2D image generation"
    }}
  ]
}}
```

## Prompt Rules (IMPORTANT!)
- MUST include "isolated on pure white background" or "on solid white background"
- Include "single object, centered"
- Include "full object visible, not cropped, entire object in frame"
- Include "no background, no shadow, no floor"
- Include "product photography style, studio lighting"

Generate EXACTLY {count} new assets for '{theme}' theme in JSON format now.
Output ONLY valid JSON. Do not include other explanations."""


REFINE_2D_PROMPT = """Enhance the following prompt for 3D asset image generation.
The image will be used to create a 3D model, so:
1. Ensure the object is isolated (white or transparent background)
2. Single object only, no scene elements
3. Good lighting for 3D reconstruction
4. Multiple viewing angles implied

Original prompt: {original_prompt}

Output ONLY the enhanced prompt, no explanations."""
