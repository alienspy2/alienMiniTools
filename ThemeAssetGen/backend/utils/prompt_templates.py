ASSET_LIST_PROMPT = """당신은 전문 3D 환경 아티스트입니다.
'{theme}' 테마의 3D 가상 공간을 구성하는 데 필요한 에셋(소품, 가구, 구조물 등) 목록을 생성해주세요.

## 요구사항
1. 최소 10개, 최대 20개의 에셋을 제안해주세요.
2. 다양한 카테고리의 에셋을 포함해주세요.
3. 각 에셋에 대해 2D 이미지 생성용 프롬프트를 작성해주세요.
4. 프롬프트는 영어로, 3D 에셋 생성에 최적화되어야 합니다.

## 카테고리
- prop: 소품 (책, 병, 접시 등)
- furniture: 가구 (의자, 테이블, 침대 등)
- wall: 벽 관련 (벽면, 문, 창문 프레임 등)
- ceiling: 천장 (조명 부착물, 장식 등)
- floor: 바닥 (타일, 카펫, 러그 등)
- decoration: 장식 (그림, 화분, 조각상 등)
- lighting: 조명 (램프, 촛대, 샹들리에 등)

## 출력 형식 (JSON)
```json
{{
  "assets": [
    {{
      "name": "영문 이름 (snake_case)",
      "name_kr": "한글 이름",
      "category": "카테고리",
      "description": "에셋에 대한 간단한 영문 설명 (1-2문장)",
      "description_kr": "에셋에 대한 간단한 한글 설명 (1-2문장)",
      "prompt_2d": "2D 이미지 생성용 영문 프롬프트 (상세하게, 배경 없이, 단일 오브젝트, 스튜디오 조명)"
    }}
  ]
}}
```

## 프롬프트 작성 규칙 (중요!)
- 반드시 "isolated on pure white background" 또는 "on solid white background" 포함
- "single object, centered" 포함
- "full object visible, not cropped, entire object in frame" 포함
- "no background, no shadow, no floor" 포함
- "product photography style, studio lighting" 포함

## 예시 (중세 성 테마)
```json
{{
  "assets": [
    {{
      "name": "royal_throne",
      "name_kr": "왕좌",
      "category": "furniture",
      "description": "An ornate medieval throne with gold decorations and red velvet cushion, fit for royalty.",
      "description_kr": "금색 장식과 빨간 벨벳 쿠션이 있는 화려한 중세 왕좌입니다. 왕실에 어울리는 가구입니다.",
      "prompt_2d": "medieval royal throne, ornate wooden chair with red velvet cushion, gold leaf decorations, carved armrests with lion heads, single object centered, full object visible, not cropped, entire object in frame, isolated on pure white background, no shadow, no floor, product photography, studio lighting, 3D asset reference, high detail"
    }}
  ]
}}
```

지금 '{theme}' 테마의 에셋 목록을 JSON 형식으로 생성해주세요.
반드시 유효한 JSON만 출력하세요. 다른 설명은 포함하지 마세요."""


ADDITIONAL_ASSETS_PROMPT = """당신은 전문 3D 환경 아티스트입니다.
'{theme}' 테마의 3D 가상 공간에 추가할 에셋 목록을 생성해주세요.

## 기존 에셋 목록 (중복 피할 것)
{existing_assets}

## 요구사항
1. 정확히 {count}개의 새로운 에셋을 제안해주세요.
2. 기존 에셋과 중복되지 않는 새로운 에셋을 제안하세요.
3. 다양한 카테고리의 에셋을 포함해주세요.
4. 각 에셋에 대해 2D 이미지 생성용 프롬프트를 작성해주세요.
5. 프롬프트는 영어로, 3D 에셋 생성에 최적화되어야 합니다.

## 카테고리
- prop: 소품 (책, 병, 접시 등)
- furniture: 가구 (의자, 테이블, 침대 등)
- wall: 벽 관련 (벽면, 문, 창문 프레임 등)
- ceiling: 천장 (조명 부착물, 장식 등)
- floor: 바닥 (타일, 카펫, 러그 등)
- decoration: 장식 (그림, 화분, 조각상 등)
- lighting: 조명 (램프, 촛대, 샹들리에 등)

## 출력 형식 (JSON)
```json
{{
  "assets": [
    {{
      "name": "영문 이름 (snake_case)",
      "name_kr": "한글 이름",
      "category": "카테고리",
      "description": "에셋에 대한 간단한 영문 설명 (1-2문장)",
      "description_kr": "에셋에 대한 간단한 한글 설명 (1-2문장)",
      "prompt_2d": "2D 이미지 생성용 영문 프롬프트 (상세하게, 배경 없이, 단일 오브젝트, 스튜디오 조명)"
    }}
  ]
}}
```

## 프롬프트 작성 규칙 (중요!)
- 반드시 "isolated on pure white background" 또는 "on solid white background" 포함
- "single object, centered" 포함
- "full object visible, not cropped, entire object in frame" 포함
- "no background, no shadow, no floor" 포함
- "product photography style, studio lighting" 포함

지금 '{theme}' 테마에 어울리는 새로운 에셋 {count}개를 JSON 형식으로 생성해주세요.
반드시 유효한 JSON만 출력하세요. 다른 설명은 포함하지 마세요."""


REFINE_2D_PROMPT = """Enhance the following prompt for 3D asset image generation.
The image will be used to create a 3D model, so:
1. Ensure the object is isolated (white or transparent background)
2. Single object only, no scene elements
3. Good lighting for 3D reconstruction
4. Multiple viewing angles implied

Original prompt: {original_prompt}

Output ONLY the enhanced prompt, no explanations."""
