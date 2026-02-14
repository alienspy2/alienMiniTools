#version 450

layout(location = 0) in vec3 frag_Normal;
layout(location = 1) in vec2 frag_UV;

layout(location = 0) out vec4 out_Color;

layout(set = 0, binding = 1) uniform MaterialData
{
    vec4 Color;
};

void main()
{
    // Hardcoded directional light (upper-right-forward)
    vec3 lightDir = normalize(vec3(0.5, 1.0, -0.5));
    vec3 normal = normalize(frag_Normal);

    // Lambert diffuse
    float ndotl = max(dot(normal, lightDir), 0.0);
    float ambient = 0.2;
    float lighting = ambient + ndotl * 0.8;

    out_Color = vec4(Color.rgb * lighting, Color.a);
}
