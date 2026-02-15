#version 450

// Shadow atlas fragment shader — writes hardware depth as R32_Float color.
// Used for all shadow types (Directional, Spot, Point) rendering into the atlas.

layout(location = 0) out float outDepth;

void main()
{
    outDepth = gl_FragCoord.z;
}
