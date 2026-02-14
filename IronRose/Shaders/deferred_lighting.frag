#version 450

layout(location = 0) in vec2 fsin_UV;
layout(location = 0) out vec4 fsout_Color;

// G-Buffer textures
layout(set = 0, binding = 0) uniform texture2D gAlbedo;
layout(set = 0, binding = 1) uniform texture2D gNormal;
layout(set = 0, binding = 2) uniform texture2D gMaterial;
layout(set = 0, binding = 3) uniform texture2D gWorldPos;
layout(set = 0, binding = 4) uniform sampler gSampler;

// Light structure (same as LightInfoGPU)
struct LightInfo
{
    vec4 PositionOrDirection;   // xyz = pos/dir, w = type (0=dir, 1=point)
    vec4 ColorIntensity;        // rgb = color, a = intensity
    vec4 Params;                // x = range
    vec4 _padding;
};

layout(set = 0, binding = 5) uniform LightingBuffer
{
    vec4 CameraPos;
    int LightCount;
    int _lpad1, _lpad2, _lpad3;
    vec4 SkyAmbient;   // rgb = sky ambient color for IBL, a = unused
    LightInfo Lights[64];
};

// Environment map
layout(set = 0, binding = 6) uniform texture2D envMap;
layout(set = 0, binding = 7) uniform EnvMapParams
{
    vec4 EnvTextureParams;   // x=hasTexture, y=exposure, z=rotation(rad), w=unused
    vec4 EnvSunDirection;    // xyz = direction toward sun
    vec4 EnvSkyParams;       // x=zenithIntensity, y=horizonIntensity, z=sunAngularRadius, w=sunIntensity
    vec4 EnvZenithColor;     // rgb = zenith color
    vec4 EnvHorizonColor;    // rgb = horizon color
};

const float PI = 3.14159265359;

// === Environment Map Sampling ===

vec2 directionToEquirectUV(vec3 dir, float rotationRad)
{
    float cosR = cos(rotationRad);
    float sinR = sin(rotationRad);
    vec3 rotDir = vec3(
        dir.x * cosR - dir.z * sinR,
        dir.y,
        dir.x * sinR + dir.z * cosR
    );

    float u = atan(rotDir.z, rotDir.x) / (2.0 * PI) + 0.5;
    float v = asin(clamp(rotDir.y, -1.0, 1.0)) / PI + 0.5;
    v = 1.0 - v;

    return vec2(u, v);
}

vec3 proceduralSky(vec3 dir)
{
    float upFactor = max(dir.y, 0.0);

    vec3 zenith = EnvZenithColor.rgb * EnvSkyParams.x;
    vec3 horizon = EnvHorizonColor.rgb * EnvSkyParams.y;

    float blend = pow(upFactor, 0.7);
    vec3 skyColor = mix(horizon, zenith, blend);

    if (dir.y < 0.0)
    {
        float downFactor = clamp(-dir.y, 0.0, 1.0);
        vec3 groundColor = horizon * 0.3;
        skyColor = mix(horizon, groundColor, pow(downFactor, 0.5));
    }

    // Sun disk
    vec3 sunDir = normalize(EnvSunDirection.xyz);
    float sunAngularRadius = EnvSkyParams.z;
    float sunIntensity = EnvSkyParams.w;

    float cosAngle = dot(dir, sunDir);
    float cosRadius = cos(sunAngularRadius);

    float sunMask = smoothstep(cosRadius - 0.002, cosRadius + 0.001, cosAngle);
    vec3 sunColor = vec3(1.0, 0.95, 0.85) * sunIntensity * sunMask;

    float glowFactor = pow(max(cosAngle, 0.0), 256.0) * sunIntensity * 0.3;
    vec3 glowColor = vec3(1.0, 0.9, 0.7) * glowFactor;

    return skyColor + sunColor + glowColor;
}

vec3 sampleEnvMap(vec3 dir)
{
    if (EnvTextureParams.x > 0.5)
    {
        vec2 uv = directionToEquirectUV(dir, EnvTextureParams.z);
        return texture(sampler2D(envMap, gSampler), uv).rgb * EnvTextureParams.y;
    }
    else
    {
        return proceduralSky(dir);
    }
}

// === PBR Functions (needed before IBL sampling) ===

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

// === Roughness-based Environment Blur (GGX Importance Sampling) ===

float radicalInverse_VdC(uint bits)
{
    bits = (bits << 16u) | (bits >> 16u);
    bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
    bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
    bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
    bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
    return float(bits) * 2.3283064365386963e-10;
}

vec2 hammersley(uint i, uint N)
{
    return vec2(float(i) / float(N), radicalInverse_VdC(i));
}

vec3 importanceSampleGGX(vec2 Xi, vec3 N, float roughness)
{
    float a = roughness * roughness;
    float phi = 2.0 * PI * Xi.x;
    float cosTheta = sqrt((1.0 - Xi.y) / (1.0 + (a * a - 1.0) * Xi.y));
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);

    // Spherical to cartesian (tangent space)
    vec3 H = vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

    // Tangent space to world space
    vec3 up = abs(N.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, N));
    vec3 bitangent = cross(N, tangent);

    return normalize(tangent * H.x + bitangent * H.y + N * H.z);
}

const uint ENV_SAMPLE_COUNT = 32u;

vec3 sampleEnvMapRough(vec3 N, vec3 V, float roughness)
{
    vec3 R = reflect(-V, N);

    // Mirror-like surfaces: single sample, no blur needed
    if (roughness < 0.05)
    {
        return sampleEnvMap(R);
    }

    bool useTexture = EnvTextureParams.x > 0.5;
    float saTexel = 1.0;
    if (useTexture)
    {
        ivec2 envSize = textureSize(sampler2D(envMap, gSampler), 0);
        saTexel = 4.0 * PI / (float(envSize.x) * float(envSize.y));
    }

    vec3 result = vec3(0.0);
    float totalWeight = 0.0;

    for (uint i = 0u; i < ENV_SAMPLE_COUNT; i++)
    {
        vec2 Xi = hammersley(i, ENV_SAMPLE_COUNT);
        vec3 H = importanceSampleGGX(Xi, R, roughness);
        vec3 L = normalize(2.0 * dot(R, H) * H - R);

        float NdotL = max(dot(N, L), 0.0);
        if (NdotL > 0.0)
        {
            vec3 envColor;

            if (useTexture)
            {
                // Per-sample mip level from GGX PDF (reduces noise)
                float D = distributionGGX(R, H, roughness);
                float pdf = D / 4.0 + 0.0001;
                float saSample = 1.0 / (float(ENV_SAMPLE_COUNT) * pdf + 0.0001);
                float mipLevel = max(0.5 * log2(saSample / saTexel), 0.0);

                // MUST use textureLod — texture() has undefined implicit LOD
                // inside non-uniform control flow (importance sampling loop)
                vec2 uv = directionToEquirectUV(L, EnvTextureParams.z);
                envColor = textureLod(sampler2D(envMap, gSampler), uv, mipLevel).rgb
                         * EnvTextureParams.y;
            }
            else
            {
                envColor = proceduralSky(L);
            }

            result += envColor * NdotL;
            totalWeight += NdotL;
        }
    }

    return totalWeight > 0.0 ? result / totalWeight : sampleEnvMap(R);
}

// === PBR Functions ===

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
    // G-Buffer sampling
    vec4 albedoData = texture(sampler2D(gAlbedo, gSampler), fsin_UV);
    vec4 normalData = texture(sampler2D(gNormal, gSampler), fsin_UV);
    vec4 materialData = texture(sampler2D(gMaterial, gSampler), fsin_UV);
    vec4 worldPosData = texture(sampler2D(gWorldPos, gSampler), fsin_UV);

    // Background pixel (no geometry written) -> skip
    if (worldPosData.a < 0.5)
    {
        fsout_Color = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    // Decode
    vec3 albedo = albedoData.rgb;
    vec3 N = normalize(normalData.rgb);
    float roughness = normalData.a;
    float metallic = materialData.r;
    float occlusion = materialData.g;
    float emissionIntensity = materialData.b;

    // World Position (direct from G-Buffer)
    vec3 worldPos = worldPosData.xyz;
    vec3 V = normalize(CameraPos.xyz - worldPos);
    float NdotV = max(dot(N, V), 0.0);

    // F0 (Fresnel reflectance at normal incidence)
    vec3 F0 = mix(vec3(0.04), albedo, metallic);

    // Multi-light accumulation
    vec3 Lo = vec3(0.0);

    for (int i = 0; i < LightCount && i < 64; i++)
    {
        float lightType = Lights[i].PositionOrDirection.w;
        vec3 lightColor = Lights[i].ColorIntensity.rgb * Lights[i].ColorIntensity.a;
        float attenuation = 1.0;
        vec3 L;

        if (lightType < 0.5)
        {
            // Directional light
            L = normalize(-Lights[i].PositionOrDirection.xyz);
        }
        else
        {
            // Point light
            vec3 toLight = Lights[i].PositionOrDirection.xyz - worldPos;
            float dist = length(toLight);
            L = toLight / max(dist, 0.001);
            float lightRange = Lights[i].Params.x;
            attenuation = max(1.0 - (dist / lightRange), 0.0);
            attenuation *= attenuation;
        }

        vec3 H = normalize(V + L);
        float NdotL = max(dot(N, L), 0.0);

        // Cook-Torrance BRDF
        float NDF = distributionGGX(N, H, roughness);
        float G = geometrySmith(N, V, L, roughness);
        vec3 F = fresnelSchlick(max(dot(H, V), 0.0), F0);

        vec3 specular = (NDF * G * F) /
            (4.0 * max(dot(N, V), 0.0) * NdotL + 0.0001);

        vec3 kD = (vec3(1.0) - F) * (1.0 - metallic);

        Lo += (kD * albedo / PI + specular) * lightColor * NdotL * attenuation;
    }

    // IBL Ambient with Roughness-based Environment Blur
    vec3 envSpecular = sampleEnvMapRough(N, V, roughness);
    vec3 envDiffuse = sampleEnvMap(N);

    vec3 F_roughness = F0 + (max(vec3(1.0 - roughness), F0) - F0) * pow(clamp(1.0 - NdotV, 0.0, 1.0), 5.0);
    vec3 kD_ambient = (vec3(1.0) - F_roughness) * (1.0 - metallic);

    vec3 ambient_diffuse = kD_ambient * albedo * envDiffuse * occlusion;
    vec3 ambient_specular = F_roughness * envSpecular * occlusion;
    vec3 ambient = ambient_diffuse + ambient_specular;
    vec3 color = ambient + Lo + emissionIntensity * albedo;

    // HDR linear output — tone mapping in Post-Processing
    fsout_Color = vec4(color, 1.0);
}
