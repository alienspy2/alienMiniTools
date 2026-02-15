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
    vec4 PositionOrDirection;   // xyz = position, w = type (1=point)
    vec4 ColorIntensity;        // rgb = color, a = intensity
    vec4 Params;                // x = range
    vec4 SpotDirection;
};

layout(set = 1, binding = 0) uniform LightVolumeData
{
    mat4 WorldViewProjection;
    mat4 LightViewProjection;
    vec4 CameraPos;
    vec4 ScreenParams;          // x=width, y=height
    vec4 ShadowParams;          // x=hasShadow, y=bias
    vec4 ShadowAtlasParams;    // xy=tileOffset, zw=tileScale (unused for point, use FaceAtlasParams)
    LightInfo Light;
    mat4 FaceVPs[6];            // 6 cubemap face view-projection matrices
    vec4 FaceAtlasParams[6];   // xy=tileOffset, zw=tileScale per face
};

// Shadow atlas (2D texture — all shadow maps tiled)
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

// Determine cubemap face from direction vector (light → fragment)
// Matches _cubeFaceTargets order: +X=0, -X=1, +Y=2, -Y=3, +Z=4, -Z=5
int getDominantFace(vec3 dir)
{
    vec3 a = abs(dir);
    if (a.x >= a.y && a.x >= a.z)
        return dir.x > 0.0 ? 0 : 1;
    if (a.y >= a.x && a.y >= a.z)
        return dir.y > 0.0 ? 2 : 3;
    return dir.z > 0.0 ? 4 : 5;
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

    // Point light attenuation
    vec3 lightPos = Light.PositionOrDirection.xyz;
    vec3 toLight = lightPos - worldPos;
    float dist = length(toLight);
    vec3 L = toLight / max(dist, 0.001);
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

    // Shadow atlas lookup (face-based, replaces cubemap)
    float shadow = 1.0;
    if (ShadowParams.x > 0.5)
    {
        vec3 lightToFrag = worldPos - lightPos;
        int face = getDominantFace(lightToFrag);

        vec4 lightSpacePos = FaceVPs[face] * vec4(worldPos, 1.0);
        vec3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
        projCoords.xy = projCoords.xy * 0.5 + 0.5;

        if (projCoords.z <= 1.0)
        {
            vec2 atlasUV = projCoords.xy * FaceAtlasParams[face].zw + FaceAtlasParams[face].xy;

            // PCF 3x3
            shadow = 0.0;
            vec2 texelSize = 1.0 / textureSize(sampler2D(ShadowMap, ShadowSampler), 0);
            for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                    float closestDepth = texture(sampler2D(ShadowMap, ShadowSampler),
                        atlasUV + vec2(x, y) * texelSize).r;
                    shadow += projCoords.z - ShadowParams.y > closestDepth ? 0.0 : 1.0;
                }
            }
            shadow /= 9.0;
        }
    }

    vec3 Lo = (kD * albedo / PI + specular) * lightColor * NdotL * attenuation * shadow;

    fsout_Color = vec4(Lo, 0.0);
}
