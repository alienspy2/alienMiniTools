# Phase 7: Deferred Rendering & PBR

## 목표
고급 렌더링 파이프라인을 구축하여 현대적인 게임 그래픽을 지원합니다.

---

## 작업 항목

### 7.1 G-Buffer 생성

**G-Buffer 레이아웃:**
- **RT0 (Albedo)**: RGB: Base Color, A: Transmission/Alpha
- **RT1 (Normal)**: RGB: World Normal (Octahedron encoding 권장), A: Smoothness
- **RT2 (Material)**: R: Metallic, G: Occlusion, B: Emission, A: Unused
- **Depth Buffer**: D32_Float_S8_UInt (Hardware Depth)

**GBuffer.cs (IronRose.Rendering):**
```csharp
using Veldrid;

namespace IronRose.Rendering
{
    public class GBuffer
    {
        public Texture AlbedoTexture { get; private set; } = null!;
        public Texture NormalTexture { get; private set; } = null!;
        public Texture MaterialTexture { get; private set; } = null!;
        public Texture DepthTexture { get; private set; } = null!;

        public Framebuffer Framebuffer { get; private set; } = null!;

        public void Initialize(GraphicsDevice device, uint width, uint height)
        {
            // Albedo (RGBA8)
            AlbedoTexture = device.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    width, height, 1, 1,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.RenderTarget | TextureUsage.Sampled
                )
            );

            // Normal + Smoothness (RGBA8)
            NormalTexture = device.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    width, height, 1, 1,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.RenderTarget | TextureUsage.Sampled
                )
            );

            // Material (Metallic, Occlusion, Emission)
            MaterialTexture = device.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    width, height, 1, 1,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.RenderTarget | TextureUsage.Sampled
                )
            );

            // Depth-Stencil
            DepthTexture = device.ResourceFactory.CreateTexture(
                TextureDescription.Texture2D(
                    width, height, 1, 1,
                    PixelFormat.D32_Float_S8_UInt,
                    TextureUsage.DepthStencil
                )
            );

            // Framebuffer 생성
            Framebuffer = device.ResourceFactory.CreateFramebuffer(
                new FramebufferDescription(
                    DepthTexture,
                    AlbedoTexture,
                    NormalTexture,
                    MaterialTexture
                )
            );
        }

        public void Dispose()
        {
            Framebuffer?.Dispose();
            AlbedoTexture?.Dispose();
            NormalTexture?.Dispose();
            MaterialTexture?.Dispose();
            DepthTexture?.Dispose();
        }
    }
}
```

---

### 7.2 Geometry Pass 셰이더

**Shaders/deferred_geometry.vert:**
```glsl
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec3 Normal;
layout(location = 2) in vec2 UV;

layout(set = 0, binding = 0) uniform WorldBuffer
{
    mat4 World;
};

layout(set = 0, binding = 1) uniform ViewBuffer
{
    mat4 View;
    mat4 Projection;
};

layout(location = 0) out vec3 fsin_WorldPos;
layout(location = 1) out vec3 fsin_Normal;
layout(location = 2) out vec2 fsin_UV;

void main()
{
    vec4 worldPos = World * vec4(Position, 1.0);
    fsin_WorldPos = worldPos.xyz;
    fsin_Normal = mat3(World) * Normal;
    fsin_UV = UV;

    gl_Position = Projection * View * worldPos;
}
```

**Shaders/deferred_geometry.frag:**
```glsl
#version 450

layout(location = 0) in vec3 fsin_WorldPos;
layout(location = 1) in vec3 fsin_Normal;
layout(location = 2) in vec2 fsin_UV;

layout(location = 0) out vec4 gAlbedo;
layout(location = 1) out vec4 gNormal;
layout(location = 2) out vec4 gMaterial;

layout(set = 1, binding = 0) uniform MaterialBuffer
{
    vec4 BaseColor;
    float Metallic;
    float Smoothness;
    float Occlusion;
    float Emission;
};

layout(set = 1, binding = 1) uniform texture2D AlbedoMap;
layout(set = 1, binding = 2) uniform sampler AlbedoSampler;

void main()
{
    // Albedo
    vec4 albedo = BaseColor * texture(sampler2D(AlbedoMap, AlbedoSampler), fsin_UV);
    gAlbedo = albedo;

    // Normal (normalized)
    vec3 normal = normalize(fsin_Normal);
    gNormal = vec4(normal * 0.5 + 0.5, Smoothness);

    // Material Properties
    gMaterial = vec4(Metallic, Occlusion, Emission, 1.0);
}
```

---

### 7.3 Lighting Pass (PBR)

**Shaders/deferred_lighting.vert:**
```glsl
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec2 UV;

layout(location = 0) out vec2 fsin_UV;

void main()
{
    fsin_UV = UV;
    gl_Position = vec4(Position, 0.0, 1.0);
}
```

**Shaders/deferred_lighting.frag (PBR):**
```glsl
#version 450

layout(location = 0) in vec2 fsin_UV;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2D gAlbedo;
layout(set = 0, binding = 1) uniform texture2D gNormal;
layout(set = 0, binding = 2) uniform texture2D gMaterial;
layout(set = 0, binding = 3) uniform texture2D gDepth;
layout(set = 0, binding = 4) uniform sampler gSampler;

layout(set = 0, binding = 5) uniform LightingBuffer
{
    vec3 ViewPosition;
    vec3 LightDirection;
    vec3 LightColor;
    float LightIntensity;
};

const float PI = 3.14159265359;

// Fresnel (Schlick approximation)
vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

// Normal Distribution Function (GGX/Trowbridge-Reitz)
float distributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float nom = a2;
    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return nom / denom;
}

// Geometry Function (Smith's method)
float geometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;

    float nom = NdotV;
    float denom = NdotV * (1.0 - k) + k;

    return nom / denom;
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float ggx2 = geometrySchlickGGX(NdotV, roughness);
    float ggx1 = geometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}

void main()
{
    // G-Buffer 샘플링
    vec4 albedo = texture(sampler2D(gAlbedo, gSampler), fsin_UV);
    vec4 normalData = texture(sampler2D(gNormal, gSampler), fsin_UV);
    vec4 material = texture(sampler2D(gMaterial, gSampler), fsin_UV);

    // 데이터 디코딩
    vec3 N = normalize(normalData.rgb * 2.0 - 1.0);
    float smoothness = normalData.a;
    float roughness = 1.0 - smoothness;
    float metallic = material.r;
    float occlusion = material.g;
    float emission = material.b;

    // PBR 계산
    vec3 V = normalize(ViewPosition); // TODO: World Position에서 계산
    vec3 L = normalize(LightDirection);
    vec3 H = normalize(V + L);

    // F0 (Fresnel reflectance at normal incidence)
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo.rgb, metallic);

    // Cook-Torrance BRDF
    float NDF = distributionGGX(N, H, roughness);
    float G = geometrySmith(N, V, L, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0) + 0.0001;
    vec3 specular = numerator / denominator;

    // kD (diffuse component)
    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;

    // Radiance
    float NdotL = max(dot(N, L), 0.0);
    vec3 Lo = (kD * albedo.rgb / PI + specular) * LightColor * LightIntensity * NdotL;

    // Ambient (간단한 구현)
    vec3 ambient = vec3(0.03) * albedo.rgb * occlusion;

    vec3 color = ambient + Lo + emission * albedo.rgb;

    // Tone mapping (ACES)
    color = color / (color + vec3(1.0));

    // Gamma correction
    color = pow(color, vec3(1.0 / 2.2));

    fsout_Color = vec4(color, 1.0);
}
```

---

### 7.4 Post-Processing

**Bloom (간단한 구현):**
```glsl
// Shaders/bloom_threshold.frag
#version 450

layout(location = 0) in vec2 fsin_UV;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2D SourceTexture;
layout(set = 0, binding = 1) uniform sampler SourceSampler;

uniform float Threshold = 1.0;

void main()
{
    vec3 color = texture(sampler2D(SourceTexture, SourceSampler), fsin_UV).rgb;
    float brightness = dot(color, vec3(0.2126, 0.7152, 0.0722));

    if (brightness > Threshold)
        fsout_Color = vec4(color, 1.0);
    else
        fsout_Color = vec4(0.0, 0.0, 0.0, 1.0);
}
```

**Gaussian Blur + Tone Mapping:**
```csharp
// PostProcessing.cs
public class PostProcessing
{
    public void ApplyBloom(CommandList cl, Texture source, Texture target)
    {
        // 1. Threshold pass
        // 2. Gaussian blur (horizontal + vertical)
        // 3. Additive blend with original
    }

    public void ApplyToneMapping(CommandList cl, Texture hdrTexture, Texture ldrTarget)
    {
        // ACES tone mapping
    }
}
```

---

## 검증 기준

✅ 금속/플라스틱 재질이 물리적으로 정확하게 렌더링됨
✅ 동적 조명이 수백 개 추가되어도 60 FPS 유지
✅ Bloom, Tone Mapping 효과 적용

---

## 예상 소요 시간
**6-8일**

---

## 다음 단계
→ [Phase 8: AI 통합](Phase8_AIIntegration.md)
