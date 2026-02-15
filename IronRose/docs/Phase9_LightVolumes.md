# Phase 9: Light Volume Rendering + Shadow Mapping

> **목표**: Deferred 라이팅 패스를 **라이트별 볼륨 렌더링**으로 전환하고, **Shadow Mapping**을 추가하여 라이트 스케일링과 시각 품질을 동시에 개선한다.

**핵심 원칙:**
- Point Light → Sphere 메시, Directional Light → 풀스크린 삼각형
- 라이트당 1 draw call, Additive Blending으로 누적
- Directional → Orthographic Shadow Map, Point → Cubemap Shadow Map
- **그림자 개별 퀄리티보다 다수 라이트의 그림자 동시 처리를 우선** — 저해상도·단순 PCF로 충분, 대신 shadow 캐스팅 라이트 수 제한 없이 스케일
- 기존 GBuffer·HDR·PostProcessing 파이프라인 유지

---

## 현재 상태 (Phase 8 완료 기준)

### Deferred Lighting 흐름

```
Geometry Pass → GBuffer (4 RT + Depth)
    ↓
Lighting Pass → HDR (풀스크린 삼각형 1회, 모든 라이트 루프)
    ↓
Skybox → HDR
    ↓
Forward (Sprite/Text/Wireframe) → HDR
    ↓
Post-Processing → Swapchain
```

**문제점**: `deferred_lighting.frag`에서 `for (i < LightCount && i < 64)` 루프를 **모든 픽셀**이 실행. 화면 구석의 픽셀도 반대편 Point Light를 계산함.

### 관련 파일

| 파일 | 역할 |
|------|------|
| `src/IronRose.Engine/RenderSystem.cs` | 전체 렌더 파이프라인 (L453-515: Render) |
| `Shaders/deferred_lighting.vert` | 풀스크린 삼각형 (하드코딩 3 vertices) |
| `Shaders/deferred_lighting.frag` | GBuffer 샘플링 + PBR 라이팅 + IBL |
| `src/IronRose.Rendering/GBuffer.cs` | Albedo/Normal/Material/WorldPos/Depth(D32_S8) |
| `src/IronRose.Engine/RoseEngine/Light.cs` | LightType (Directional, Point), color/intensity/range |
| `src/IronRose.Engine/RoseEngine/PrimitiveGenerator.cs` | `CreateSphere()` 재사용 가능 |

---

## 설계

### 새로운 렌더 흐름

```
1. Geometry Pass   → GBuffer
2. Shadow Pass     → Shadow Maps  (라이트별: Directional→2D, Point→Cubemap)
3. Ambient Pass    → HDR  (풀스크린, IBL + ambient, Overwrite)
4. Directional     → HDR  (풀스크린 × N개, Additive, shadow 적용)
5. Point Lights    → HDR  (Sphere × N개, Additive, shadow 적용)
6. Skybox          → HDR
7. Forward         → HDR
8. Post-Processing → Swapchain
```

### 핵심 기법: Back-Face + GreaterEqual

Point Light 볼륨 렌더링에 **back-face rendering** 기법 사용:

```
Rasterizer:   FaceCullMode.Front  (back-face만 렌더)
Depth Test:   GreaterEqual        (back-face가 씬보다 뒤에 있으면 pass)
Depth Write:  OFF
Blend:        Additive (One + One)
```

**동작 원리:**
- Sphere의 back-face가 씬 지오메트리보다 **뒤에** 있으면 → 해당 픽셀은 라이트 볼륨 **내부** → 셰이딩
- Sphere의 back-face가 씬 지오메트리보다 **앞에** 있으면 → 해당 픽셀은 볼륨 **외부** → 스킵
- 카메라가 볼륨 내부에 있어도 정상 작동 (back-face는 항상 카메라 앞에 있으므로)

> GBuffer의 Depth 포맷이 `D32_Float_S8_UInt`이고, HDR 프레임버퍼가 이 depth를 공유하므로 추가 depth copy 불필요.

---

## 구현 계획

### 9.1 리소스 레이아웃 분리

현재 단일 레이아웃(`_deferredLightingLayout`, set 0에 GBuffer + 라이트 + envmap 전부)을 **2-set 구조**로 분리:

**Set 0 — GBuffer (모든 라이팅 패스 공유)**
```
binding 0: gAlbedo     (TextureReadOnly)
binding 1: gNormal     (TextureReadOnly)
binding 2: gMaterial   (TextureReadOnly)
binding 3: gWorldPos   (TextureReadOnly)
binding 4: gSampler    (Sampler)
```

**Set 1 — Ambient Pass 전용**
```
binding 0: AmbientBuffer  (UBO: CameraPos + SkyAmbient)
binding 1: EnvMap          (TextureReadOnly, Cubemap)
binding 2: EnvMapParams    (UBO)
```

**Set 1 — Direct Light Pass 전용 (Directional + Point 공용)**
```
binding 0: LightVolumeBuffer  (UBO: 아래 구조체)
```

```csharp
[StructLayout(LayoutKind.Sequential)]
struct LightVolumeUniforms
{
    Matrix4x4 WorldViewProjection;   // 64B  Point: sphere MVP, Directional: 미사용
    Matrix4x4 LightViewProjection;   // 64B  Shadow Map 좌표 변환 (Directional용)
    Vector4   CameraPos;             // 16B
    Vector4   ScreenParams;          // 16B  x=width, y=height (gl_FragCoord → UV 변환)
    Vector4   ShadowParams;          // 16B  x=hasShadow(0/1), y=bias, z=normalBias, w=shadowStrength
    LightInfoGPU Light;              // 64B  단일 라이트
}
// Total: 240 bytes
```

### 9.2 파이프라인 생성

기존 `_lightingPipeline` 1개를 **4개**로 교체:

| 파이프라인 | Vertex Shader | Fragment Shader | Cull | Depth | Blend | 비고 |
|-----------|--------------|----------------|------|-------|-------|------|
| `_shadowPipeline` | `shadow.vert` (신규) | (없음/빈 frag) | Back | LessEqual, WriteOn | — | Depth-only |
| `_ambientPipeline` | `deferred_lighting.vert` (기존) | `deferred_ambient.frag` (신규) | None | Disabled | Overwrite | |
| `_directionalLightPipeline` | `deferred_lighting.vert` (기존) | `deferred_directlight.frag` (신규) | None | Disabled | **Additive** | |
| `_pointLightPipeline` | `deferred_pointlight.vert` (신규) | `deferred_pointlight.frag` (신규) | **Front** | **GreaterEqual, WriteOff** | **Additive** | |

> Point Light Cubemap Shadow에는 `_shadowPipeline`을 6회 (face당 1회) 실행. Geometry Shader 없이 멀티패스로 처리하여 호환성 유지.

### 9.3 셰이더 작성

#### `deferred_ambient.frag` (신규)

기존 `deferred_lighting.frag`에서 IBL/ambient 부분만 추출:
- GBuffer 샘플링 + decode
- `sampleEnvMapRough()`, `sampleEnvMapDiffuse()`, `envBRDFApprox()` 유지
- `for (i < LightCount)` 루프 **제거**
- 출력: `ambient_diffuse + ambient_specular + emission`

#### `deferred_directlight.frag` (신규)

풀스크린 삼각형, 단일 Directional Light:
- GBuffer 샘플링 + decode
- 단일 라이트 Cook-Torrance BRDF 계산
- UV: `fsin_UV` (vertex shader에서 전달)
- Shadow Map 샘플링 (2D texture, LightViewProjection으로 UV 계산)

#### `deferred_pointlight.vert` (신규)

```glsl
layout(set = 1, binding = 0) uniform LightVolumeData {
    mat4 WorldViewProjection;
    vec4 CameraPos;
    vec4 ScreenParams;
    // ... LightInfo
};

layout(location = 0) in vec3 Position;

void main() {
    gl_Position = WorldViewProjection * vec4(Position, 1.0);
}
```

- 입력: sphere 메시 vertex (Position만 사용, Normal/UV 무시)
- 출력: 클립 좌표만 (fragment에서 `gl_FragCoord` 사용)

#### `deferred_pointlight.frag` (신규)

```glsl
void main() {
    vec2 uv = gl_FragCoord.xy / ScreenParams.xy;

    // GBuffer 샘플링 + decode
    // worldPosData.a < 0.5 → discard (배경 픽셀)

    // Shadow: cubemap shadow map 샘플링
    vec3 lightToFrag = worldPos - lightPos;
    float closestDepth = texture(shadowCubeMap, lightToFrag).r * farPlane;
    float currentDepth = length(lightToFrag);
    float shadow = currentDepth - bias > closestDepth ? 0.0 : 1.0;

    // 단일 Point Light Cook-Torrance BRDF × shadow
}
```

#### `shadow.vert` (신규)

Shadow Map 생성용 depth-only vertex shader:
```glsl
layout(set = 0, binding = 0) uniform ShadowTransforms {
    mat4 LightMVP;
};
layout(location = 0) in vec3 Position;

void main() {
    gl_Position = LightMVP * vec4(Position, 1.0);
}
```

#### `shadow_point.frag` (신규)

Point Light cubemap shadow용 — 선형 depth 기록:
```glsl
layout(location = 0) in vec3 fragWorldPos;
layout(location = 0) out float outDepth;

uniform vec4 LightPosAndFarPlane;  // xyz=lightPos, w=farPlane

void main() {
    float dist = length(fragWorldPos - LightPosAndFarPlane.xyz);
    outDepth = dist / LightPosAndFarPlane.w;  // [0,1] 정규화
}
```

### 9.4 Sphere 메시 준비

`PrimitiveGenerator.CreateSphere()` 재사용 (radius=0.5 기본):
- 라이트 볼륨용으로 저해상도 sphere 생성 (lon=12, lat=8 정도면 충분)
- `Initialize()`에서 1회 생성, GPU 업로드, 전체 Point Light 공유
- MVP에서 scale = `light.range * 2` (radius 0.5 → diameter 1.0이므로 range로 스케일)

### 9.5 Render() 수정

`RenderSystem.Render()` (L453-515) 변경:

```csharp
// === 2. Ambient/IBL Pass → HDR (Overwrite) ===
cl.SetPipeline(_ambientPipeline);
cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
cl.SetGraphicsResourceSet(1, _ambientResourceSet);
cl.Draw(3, 1, 0, 0);

// === 3. Direct Lights → HDR (Additive) ===
foreach (var light in Light._allLights)
{
    if (!light.enabled || !light.gameObject.activeInHierarchy) continue;

    UploadSingleLightUniforms(cl, light, viewProj);

    if (light.type == LightType.Directional)
    {
        cl.SetPipeline(_directionalLightPipeline);
        cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
        cl.SetGraphicsResourceSet(1, _lightVolumeResourceSet);
        cl.Draw(3, 1, 0, 0);
    }
    else // Point
    {
        cl.SetPipeline(_pointLightPipeline);
        cl.SetGraphicsResourceSet(0, _gBufferResourceSet);
        cl.SetGraphicsResourceSet(1, _lightVolumeResourceSet);
        cl.SetVertexBuffer(0, _lightSphereMesh.VertexBuffer);
        cl.SetIndexBuffer(_lightSphereMesh.IndexBuffer, IndexFormat.UInt32);
        cl.DrawIndexed((uint)_lightSphereMesh.indices.Length);
    }
}
```

### 9.6 Shadow Map 리소스

#### Light 컴포넌트 확장

```csharp
// Light.cs에 추가
public bool shadows { get; set; } = false;
public int shadowResolution { get; set; } = 1024;
public float shadowBias { get; set; } = 0.005f;
public float shadowNormalBias { get; set; } = 0.02f;
public float shadowNearPlane { get; set; } = 0.1f;
```

#### Directional Light Shadow Map

- **2D Depth Texture** (`D32_Float`)
- Orthographic projection, 카메라 위치 기준 frustum 계산
- 해상도: `light.shadowResolution × light.shadowResolution` (기본 1024)

```csharp
// 카메라 중심으로 orthographic bounds 계산
float shadowRange = 20f;  // 씬 크기에 따라 조절
var lightView = Matrix4x4.CreateLookAt(camPos - lightDir * shadowRange, camPos, Vector3.UnitY);
var lightProj = Matrix4x4.CreateOrthographic(shadowRange * 2, shadowRange * 2, 0.1f, shadowRange * 2);
var lightVP = lightView * lightProj;
```

#### Point Light Shadow Map

- **Cubemap Depth Texture** (`R32_Float`, 6면)
- Perspective projection (FOV=90°, aspect=1.0) × 6 방향
- Geometry Shader 없이 **6-pass 렌더링** (호환성 우선)

```csharp
// 6 face view matrices
Matrix4x4[] faceViews = new Matrix4x4[6];
faceViews[0] = Matrix4x4.CreateLookAt(lightPos, lightPos + Vector3.UnitX,  -Vector3.UnitY); // +X
faceViews[1] = Matrix4x4.CreateLookAt(lightPos, lightPos - Vector3.UnitX,  -Vector3.UnitY); // -X
faceViews[2] = Matrix4x4.CreateLookAt(lightPos, lightPos + Vector3.UnitY,   Vector3.UnitZ); // +Y
faceViews[3] = Matrix4x4.CreateLookAt(lightPos, lightPos - Vector3.UnitY,  -Vector3.UnitZ); // -Y
faceViews[4] = Matrix4x4.CreateLookAt(lightPos, lightPos + Vector3.UnitZ,  -Vector3.UnitY); // +Z
faceViews[5] = Matrix4x4.CreateLookAt(lightPos, lightPos - Vector3.UnitZ,  -Vector3.UnitY); // -Z

var faceProj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, near, light.range);
```

- 각 face는 별도 `Framebuffer`로 렌더 (cubemap face를 color attachment로 bind)
- Fragment shader에서 **선형 distance** 기록 (`length(fragWorldPos - lightPos) / farPlane`)

#### Shadow Map 관리

```csharp
class ShadowData
{
    public Texture? ShadowMap;          // Directional: Texture2D, Point: TextureCube
    public TextureView? ShadowView;
    public Framebuffer? ShadowFB;       // Directional: 1개, Point: 6개 (face별)
    public Framebuffer[]? CubeFBs;      // Point Light용 6-face framebuffers
    public Matrix4x4 LightVP;          // Directional용
    public Matrix4x4[] FaceVPs;        // Point용 (6개)
}
```

- Shadow를 캐스팅하는 라이트만 ShadowData 생성 (`light.shadows == true`)
- `Initialize()`에서 shadow pipeline + layout 생성
- 매 프레임 shadow 캐스팅 라이트만 shadow pass 실행

### 9.7 Shadow Pass 렌더링

Render() 흐름에서 Geometry Pass 직후, Ambient Pass 직전:

```csharp
// === 2. Shadow Pass ===
foreach (var light in Light._allLights)
{
    if (!light.enabled || !light.shadows) continue;

    if (light.type == LightType.Directional)
    {
        // Orthographic shadow map: 전체 씬을 라이트 시점에서 depth 렌더
        cl.SetFramebuffer(shadowData.ShadowFB);
        cl.ClearDepthStencil(1f);
        cl.SetPipeline(_shadowPipeline);
        foreach (var renderer in MeshRenderer._allRenderers)
            DrawShadowCaster(cl, renderer, shadowData.LightVP);
    }
    else if (light.type == LightType.Point)
    {
        // Cubemap shadow: 6면 각각 depth 렌더
        for (int face = 0; face < 6; face++)
        {
            cl.SetFramebuffer(shadowData.CubeFBs[face]);
            cl.ClearColorTarget(0, RgbaFloat.White);  // depth=1 (최대)
            cl.SetPipeline(_shadowPointPipeline);
            foreach (var renderer in MeshRenderer._allRenderers)
                DrawShadowCaster(cl, renderer, shadowData.FaceVPs[face]);
        }
    }
}
```

### 9.8 셰이더 Shadow 샘플링

#### Directional Light — 2D Shadow Map

```glsl
// deferred_directlight.frag
uniform sampler2DShadow shadowMap;  // 또는 sampler2D + 수동 비교

float calcShadow(vec3 worldPos) {
    if (ShadowParams.x < 0.5) return 1.0;  // shadow 비활성

    vec4 lightSpacePos = LightViewProjection * vec4(worldPos, 1.0);
    vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    projCoords = projCoords * 0.5 + 0.5;  // [-1,1] → [0,1]

    if (projCoords.z > 1.0) return 1.0;  // frustum 밖

    // PCF 3×3 (soft shadow)
    float shadow = 0.0;
    vec2 texelSize = 1.0 / textureSize(shadowMap, 0);
    for (int x = -1; x <= 1; x++) {
        for (int y = -1; y <= 1; y++) {
            float closestDepth = texture(shadowMap, projCoords.xy + vec2(x,y) * texelSize).r;
            shadow += projCoords.z - ShadowParams.y > closestDepth ? 0.0 : 1.0;
        }
    }
    return shadow / 9.0;
}
```

#### Point Light — Cubemap Shadow Map

```glsl
// deferred_pointlight.frag
uniform samplerCube shadowCubeMap;

float calcPointShadow(vec3 worldPos, vec3 lightPos, float farPlane) {
    if (ShadowParams.x < 0.5) return 1.0;

    vec3 fragToLight = worldPos - lightPos;
    float closestDepth = texture(shadowCubeMap, fragToLight).r;  // [0,1] 정규화
    float currentDepth = length(fragToLight) / farPlane;

    // PCF: 방향 오프셋 (20-sample disc)
    float shadow = 0.0;
    float diskRadius = (1.0 + length(CameraPos.xyz - worldPos) / farPlane) / 50.0;
    vec3 sampleOffsetDirections[20] = vec3[]( /* 20개 방향 벡터 */ );
    for (int i = 0; i < 20; i++) {
        float depth = texture(shadowCubeMap, fragToLight + sampleOffsetDirections[i] * diskRadius).r;
        shadow += currentDepth - ShadowParams.y > depth ? 0.0 : 1.0;
    }
    return shadow / 20.0;
}
```

### 9.9 Lighting Resource Set 확장

Direct Light set (set 1)에 shadow map 바인딩 추가:

**Set 1 — Direct Light Pass (업데이트)**
```
binding 0: LightVolumeBuffer    (UBO)
binding 1: ShadowMap            (TextureReadOnly, 2D 또는 Cube)
binding 2: ShadowSampler        (Sampler, ComparisonLessEqual)
```

> Directional과 Point가 다른 텍스처 타입을 사용하므로, 각각 별도 resource layout + resource set 필요:
> - `_directionalLightLayout`: set 1에 UBO + Texture2D + Sampler
> - `_pointLightLayout`: set 1에 UBO + TextureCube + Sampler

### 9.10 정리

- `MaxDeferredLights` 상수 제거 (더 이상 배열 제한 없음)
- `DeferredLightHeader`, `_deferredLights[]` 제거
- `UploadDeferredLightData()` → `UploadSingleLightUniforms()` 교체
- 기존 `_lightingPipeline`, `_lightingShaders` 제거
- `deferred_lighting.frag`의 PBR 함수들은 새 셰이더에 복사 (GLSL include 없음)
- Shadow 미활성 라이트는 shadow pass 스킵 + 셰이더에서 `ShadowParams.x < 0.5` early-out

---

## 수정 대상 파일

| 파일 | 변경 내용 |
|------|----------|
| `src/IronRose.Engine/RenderSystem.cs` | 파이프라인 4개 분리, Shadow Pass 추가, Render() 루프 변경, 리소스 레이아웃/셋 재구성 |
| `src/IronRose.Engine/RoseEngine/Light.cs` | `shadows`, `shadowResolution`, `shadowBias`, `shadowNormalBias` 프로퍼티 추가 |
| `Shaders/shadow.vert` | **신규** — Shadow depth-only vertex shader |
| `Shaders/shadow_point.frag` | **신규** — Point Light 선형 depth 기록 |
| `Shaders/deferred_ambient.frag` | **신규** — IBL + ambient 전용 |
| `Shaders/deferred_directlight.frag` | **신규** — 단일 Directional Light PBR + shadow sampling |
| `Shaders/deferred_pointlight.vert` | **신규** — Sphere MVP 변환 |
| `Shaders/deferred_pointlight.frag` | **신규** — 단일 Point Light PBR + cubemap shadow sampling |
| `Shaders/deferred_lighting.vert` | 유지 (ambient + directional용) |
| `Shaders/deferred_lighting.frag` | **삭제** (ambient + directlight으로 분리) |

---

## 검증

1. `dotnet build` 성공 확인
2. **CornellBoxDemo**: Directional + Point Light 혼합 씬에서 기존과 동일한 렌더링 결과 (shadow OFF)
3. **PBRDemo**: 다수 오브젝트에서 IBL + Direct Light 정상 동작 (shadow OFF)
4. 라이트 64개 이상 추가 테스트 (MaxDeferredLights 제한 해제 확인)
5. 카메라를 Point Light 내부로 이동 → 정상 렌더링 확인
6. Directional Light `shadows = true` → 오브젝트 그림자 확인
7. Point Light `shadows = true` → 전방향 그림자 확인 (6면)
8. Shadow bias 조절 → shadow acne / peter-panning 없는 적절한 값 확인
9. PCF soft shadow 시각 품질 확인

---

## 향후 확장 (이 Phase 범위 밖)

- **Cascaded Shadow Maps (CSM)**: Directional Light 거리별 해상도 분리 (근거리 고해상도)
- **Spot Light**: Cone 메시 볼륨 + 단일 2D Shadow Map
- **Shadow Atlas**: 여러 라이트 shadow map을 하나의 큰 텍스처에 타일링
- **Tiled/Clustered Shading**: 수천 개 라이트 대응 (compute shader 기반)
- **Contact Shadows**: Screen-space ray marching으로 근접 그림자 보강
