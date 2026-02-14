#version 450

// Fullscreen triangle (no vertex buffer needed)
vec2 positions[3] = vec2[](
    vec2(-1.0, -1.0),
    vec2( 3.0, -1.0),
    vec2(-1.0,  3.0)
);

layout(set = 0, binding = 0) uniform SkyboxUniforms
{
    mat4 InverseViewProjection;
    vec4 SunDirection;    // xyz = direction toward sun, w = unused
    vec4 SkyParams;       // x = zenithIntensity, y = horizonIntensity, z = sunAngularRadius, w = sunIntensity
    vec4 ZenithColor;     // rgb = zenith color
    vec4 HorizonColor;    // rgb = horizon color
    vec4 TextureParams;   // x = hasTexture, y = exposure, z = rotation (radians), w = unused
};

layout(location = 0) out vec3 fsin_RayDir;

void main()
{
    vec2 pos = positions[gl_VertexIndex];
    gl_Position = vec4(pos, 1.0, 1.0); // z=1.0 → max depth after perspective divide (1.0/1.0 = 1.0)

    // Reconstruct world-space ray direction from clip-space position
    vec4 clipPos = vec4(pos, 1.0, 1.0);
    vec4 worldPos = InverseViewProjection * clipPos;
    fsin_RayDir = worldPos.xyz / worldPos.w;
}
