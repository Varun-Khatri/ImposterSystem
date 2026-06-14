#ifndef VK_IMPOSTER_INCLUDED
#define VK_IMPOSTER_INCLUDED

// Octahedral mapping where the POLE axis is object-space +Y (world up for an
// upright prop). This keeps hemi-octa coverage aligned to the upper hemisphere,
// which is what you want for ground-placed environment props.
//
// IMPORTANT: this math is mirrored 1:1 in ImposterOcta.cs. If you change a sign
// here, change it there too, or the baked atlas and the runtime sampling drift.

float2 VK_SignNotZero(float2 v)
{
    return float2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

// ---- Full sphere (Y-pole) -------------------------------------------------
float2 VK_FullOctaEncode(float3 d)
{
    d /= (abs(d.x) + abs(d.y) + abs(d.z));
    float2 p = float2(d.x, d.z);              // project onto plane orthogonal to pole(Y)
    if (d.y < 0.0)
        p = (1.0 - abs(p.yx)) * VK_SignNotZero(p);
    return p * 0.5 + 0.5;
}

float3 VK_FullOctaDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 d = float3(f.x, 1.0 - abs(f.x) - abs(f.y), f.y); // y = height
    float t = saturate(-d.y);
    d.x += d.x >= 0.0 ? -t : t;
    d.z += d.z >= 0.0 ? -t : t;
    return normalize(d);
}

// ---- Upper hemisphere (Y-pole) -------------------------------------------
float2 VK_HemiOctaEncode(float3 d)
{
    float2 p = float2(d.x, d.z) / (abs(d.x) + abs(d.z) + max(d.y, 1e-5));
    return float2(p.x + p.y, p.x - p.y) * 0.5 + 0.5;
}

float3 VK_HemiOctaDecode(float2 f)
{
    f = f * 2.0 - 1.0;
    float2 p = float2(f.x + f.y, f.x - f.y) * 0.5;
    float h = 1.0 - abs(p.x) - abs(p.y);
    return normalize(float3(p.x, h, p.y));
}

// ---- Keyword-switched wrappers -------------------------------------------
float2 VK_OctaEncode(float3 d)
{
#ifdef _IMPOSTER_HEMI
    return VK_HemiOctaEncode(d);
#else
    return VK_FullOctaEncode(d);
#endif
}

float3 VK_OctaDecode(float2 f)
{
#ifdef _IMPOSTER_HEMI
    return VK_HemiOctaDecode(f);
#else
    return VK_FullOctaDecode(f);
#endif
}

// Build a stable basis for a frame given its view direction (object space).
// upRef must not be parallel to dir; caller picks it.
void VK_FrameBasis(float3 dir, float3 upRef, out float3 right, out float3 up)
{
    right = normalize(cross(upRef, dir));
    up    = cross(dir, right);
}

#endif // VK_IMPOSTER_INCLUDED
