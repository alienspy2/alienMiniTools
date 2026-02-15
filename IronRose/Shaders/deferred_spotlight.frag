#version 450

layout(location = 0) out vec4 fsout_Color;

// Set 0 — GBuffer
layout(set = 0, binding = 0) uniform texture2D gAlbedo;
layout(set = 0, binding = 1) uniform texture2D gNormal;
layout(set = 0, binding = 2) uniform texture2D gMaterial;
layout(set = 0, binding = 3) uniform texture2D gWorldPos;
layout(set = 0, binding = 4) uniform sampler gSampler;

// Set 1 — Light volume data
struct LightInfo
{
    vec4 PositionOrDirection;   // xyz = position, w = type (2=spot)
    vec4 ColorIntensity;        // rgb = color, a = intensity
    vec4 Params;                // x = range, y = cosInnerAngle, z = cosOuterAngle
    vec4 SpotDirection;         // xyz = spot forward direction
};

layout(set = 1, binding = 0) uniform LightVolumeData
{
    mat4 WorldViewProjection;
    mat4 LightViewProjection;   // shadow map VP
    vec4 CameraPos;
    vec4 ScreenParams;          // x=width, y=height
    vec4 ShadowParams;          // x=hasShadow, y=bias
    vec4 ShadowAtlasParams;    // xy=tileOffset, zw=tileScale
    LightInfo Light;
};

// Shadow map
layout(set = 1, binding = 1) uniform texture2D ShadowMap;
layout(set = 1, binding = 2) uniform sampler ShadowSampler;

const float PI = 3.14159265359;

// === PBR Functions ===

float distributionGGX(vec3 N, vec3 H, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;

    float denom = (NdotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return a2 / denom;
}

vec3 fresnelSchlick(float cosTheta, vec3 F0)
{
    return F0 + (1.0 - F0) * pow(clamp(1.0 - cosTheta, 0.0, 1.0), 5.0);
}

float geometrySchlickGGX(float NdotV, float roughness)
{
    float r = (roughness + 1.0);
    float k = (r * r) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float geometrySmith(vec3 N, vec3 V, vec3 L, float roughness)
{
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    return geometrySchlickGGX(NdotV, roughness) * geometrySchlickGGX(NdotL, roughness);
}

void main()
{
    // Reconstruct UV from gl_FragCoord
    vec2 uv = gl_FragCoord.xy / ScreenParams.xy;

    vec4 albedoData = texture(sampler2D(gAlbedo, gSampler), uv);
    vec4 normalData = texture(sampler2D(gNormal, gSampler), uv);
    vec4 materialData = texture(sampler2D(gMaterial, gSampler), uv);
    vec4 worldPosData = texture(sampler2D(gWorldPos, gSampler), uv);

    if (worldPosData.a < 0.5)
        discard;

    vec3 albedo = albedoData.rgb;
    vec3 N = normalize(normalData.rgb);
    float roughness = normalData.a;
    float metallic = materialData.r;
    vec3 worldPos = worldPosData.xyz;
    vec3 V = normalize(CameraPos.xyz - worldPos);
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    // Spot light position and direction
    vec3 lightPos = Light.PositionOrDirection.xyz;
    vec3 spotDir = normalize(Light.SpotDirection.xyz);
    vec3 toLight = lightPos - worldPos;
    float dist = length(toLight);
    vec3 L = toLight / max(dist, 0.001);

    // Spot cone falloff
    float theta = dot(L, -spotDir);
    float cosInner = Light.Params.y;
    float cosOuter = Light.Params.z;
    float epsilon = cosInner - cosOuter;
    float spotFactor = clamp((theta - cosOuter) / epsilon, 0.0, 1.0);

    if (spotFactor <= 0.0)
        discard;

    // Distance attenuation (same as point light)
    float lightRange = Light.Params.x;
    float attenuation = max(1.0 - (dist / lightRange), 0.0);
    attenuation *= attenuation;

    if (attenuation <= 0.0)
        discard;

    vec3 lightColor = Light.ColorIntensity.rgb * Light.ColorIntensity.a;
    vec3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);

    // Cook-Torrance BRDF
    float NDF = distributionGGX(N, H, roughness);
    float G = geometrySmith(N, V, L, roughness);
    vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

    vec3 specular = (NDF * G * F) /
        (4.0 * max(dot(N, V), 0.0) * NdotL + 0.0001);

    vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

    // Shadow (atlas-based)
    float shadow = 1.0;
    if (ShadowParams.x > 0.5)
    {
        // Normal offset bias: shift sample along surface normal
        float normalOffset = ShadowParams.z * (1.0 - NdotL);
        vec3 offsetPos = worldPos + N * normalOffset;

        vec4 lightSpacePos = LightViewProjection * vec4(offsetPos, 1.0);
        vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
        projCoords.xy = projCoords.xy * 0.5 + 0.5;
        projCoords.y = 1.0 - projCoords.y;  // Veldrid/Vulkan flips viewport Y

        if (projCoords.z <= 1.0)
        {
            vec2 atlasUV = projCoords.xy * ShadowAtlasParams.zw + ShadowAtlasParams.xy;

            // Slope-scale depth bias
            float bias = ShadowParams.y + ShadowParams.y * 5.0 * (1.0 - NdotL);

            shadow = 0.0;
            vec2 texelSize = 1.0 / textureSize(sampler2D(ShadowMap, ShadowSampler), 0);
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    float closestDepth = texture(sampler2D(ShadowMap, ShadowSampler),
                        atlasUV + vec2(x, y) * texelSize).r;
                    shadow += projCoords.z - bias > closestDepth ? 0.0 : 1.0;
                }
            }
            shadow /= 9.0;
        }
    }

    vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL * attenuation * spotFactor * shadow;

    fsout_Color = vec4(Lo, 0.0);
}
