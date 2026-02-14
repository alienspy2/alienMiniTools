#version 450

layout(location = 0) in vec3 fsin_Normal;
layout(location = 1) in vec2 fsin_UV;
layout(location = 2) in vec3 fsin_WorldPos;

// MRT outputs (4)
layout(location = 0) out vec4 gAlbedo;     // RT0: R8G8B8A8_UNorm
layout(location = 1) out vec4 gNormal;     // RT1: R16G16B16A16_Float
layout(location = 2) out vec4 gMaterial;   // RT2: R8G8B8A8_UNorm
layout(location = 3) out vec4 gWorldPos;   // RT3: R16G16B16A16_Float

layout(set = 0, binding = 1) uniform MaterialData
{
    vec4 Color;
    vec4 Emission;
    float HasTexture;
    float Metallic;
    float Roughness;
    float Occlusion;
};

layout(set = 0, binding = 2) uniform texture2D MainTexture;
layout(set = 0, binding = 3) uniform sampler MainSampler;

void main()
{
    // Albedo
    vec4 texColor = vec4(1.0);
    if (HasTexture > 0.5)
    {
        texColor = texture(sampler2D(MainTexture, MainSampler), fsin_UV);
    }
    gAlbedo = Color * texColor;

    // Normal (RGBA16F — store [-1,1] directly) + Roughness
    vec3 normal = normalize(fsin_Normal);
    gNormal = vec4(normal, Roughness);

    // Material: Metallic, Occlusion, Emission intensity
    float emissionIntensity = max(Emission.r, max(Emission.g, Emission.b));
    gMaterial = vec4(Metallic, Occlusion, emissionIntensity, 1.0);

    // World position (direct storage — avoids depth reconstruction issues)
    gWorldPos = vec4(fsin_WorldPos, 1.0);
}
