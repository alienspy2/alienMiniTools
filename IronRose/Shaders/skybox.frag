#version 450

layout(location = 0) in vec3 fsin_RayDir;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform SkyboxUniforms
{
    mat4 InverseViewProjection;
    vec4 SunDirection;    // xyz = direction toward sun, w = unused
    vec4 SkyParams;       // x = zenithIntensity, y = horizonIntensity, z = sunAngularRadius, w = sunIntensity
    vec4 ZenithColor;     // rgb = zenith color
    vec4 HorizonColor;    // rgb = horizon color
    vec4 TextureParams;   // x = hasTexture, y = exposure, z = rotation (radians), w = unused
};

layout(set = 0, binding = 1) uniform texture2D SkyTexture;
layout(set = 0, binding = 2) uniform sampler SkySampler;

const float PI = 3.14159265359;

// Convert direction to equirectangular UV coordinates
vec2 directionToEquirectUV(vec3 dir, float rotationRad)
{
    // Apply Y-axis rotation
    float cosR = cos(rotationRad);
    float sinR = sin(rotationRad);
    vec3 rotDir = vec3(
        dir.x * cosR - dir.z * sinR,
        dir.y,
        dir.x * sinR + dir.z * cosR
    );

    float u = atan(rotDir.z, rotDir.x) / (2.0 * PI) + 0.5;
    float v = asin(clamp(rotDir.y, -1.0, 1.0)) / PI + 0.5;

    // Flip V so top of image = zenith
    v = 1.0 - v;

    return vec2(u, v);
}

// Procedural sky (atmospheric gradient + sun)
vec3 proceduralSky(vec3 dir)
{
    float upFactor = max(dir.y, 0.0);

    vec3 zenith = ZenithColor.rgb * SkyParams.x;
    vec3 horizon = HorizonColor.rgb * SkyParams.y;

    float blend = pow(upFactor, 0.7);
    vec3 skyColor = mix(horizon, zenith, blend);

    if (dir.y < 0.0)
    {
        float downFactor = clamp(-dir.y, 0.0, 1.0);
        vec3 groundColor = horizon * 0.3;
        skyColor = mix(horizon, groundColor, pow(downFactor, 0.5));
    }

    // Sun disk
    vec3 sunDir = normalize(SunDirection.xyz);
    float sunAngularRadius = SkyParams.z;
    float sunIntensity = SkyParams.w;

    float cosAngle = dot(dir, sunDir);
    float cosRadius = cos(sunAngularRadius);

    float sunMask = smoothstep(cosRadius - 0.002, cosRadius + 0.001, cosAngle);
    vec3 sunColor = vec3(1.0, 0.95, 0.85) * sunIntensity * sunMask;

    float glowFactor = pow(max(cosAngle, 0.0), 256.0) * sunIntensity * 0.3;
    vec3 glowColor = vec3(1.0, 0.9, 0.7) * glowFactor;

    return skyColor + sunColor + glowColor;
}

void main()
{
    vec3 dir = normalize(fsin_RayDir);

    float hasTexture = TextureParams.x;
    float exposure = TextureParams.y;
    float rotationRad = TextureParams.z;

    vec3 color;

    if (hasTexture > 0.5)
    {
        // Panoramic (equirectangular) texture sampling
        vec2 uv = directionToEquirectUV(dir, rotationRad);
        color = texture(sampler2D(SkyTexture, SkySampler), uv).rgb;
        color *= exposure;
    }
    else
    {
        // Procedural atmospheric sky
        color = proceduralSky(dir);
    }

    fsout_Color = vec4(color, 1.0);
}
