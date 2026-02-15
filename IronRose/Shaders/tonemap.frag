#version 450

layout(location = 0) in vec2 fsin_UV;
layout(location = 0) out vec4 fsout_Color;

layout(set = 0, binding = 0) uniform texture2D SourceTexture;
layout(set = 0, binding = 1) uniform sampler SourceSampler;

layout(set = 0, binding = 2) uniform TonemapParams
{
    float Exposure;
    float _pad1;
    float _pad2;
    float _pad3;
};

// ACES Filmic Tone Mapping
vec3 ACESFilm(vec3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec4 hdrSample = texture(sampler2D(SourceTexture, SourceSampler), fsin_UV);
    vec3 color = hdrSample.rgb;
    float alpha = hdrSample.a;

    // Exposure
    color *= Exposure;

    // ACES Tone Mapping
    color = ACESFilm(color);

    // Gamma correction (linear -> sRGB)
    color = pow(color, vec3(1.0 / 2.2));

    // Preserve alpha so background (alpha=0) shows the clear color
    fsout_Color = vec4(color, alpha);
}
