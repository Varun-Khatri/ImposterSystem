using UnityEngine;

namespace VK.Imposters
{
    /// <summary>
    /// CPU mirror of Shaders/Imposter.hlsl. The baker uses Decode() to aim each
    /// capture camera; the runtime shader uses the identical encode/decode to
    /// pick frames. Keep the two files in lockstep.
    /// Pole axis is object-space +Y.
    /// </summary>
    public static class ImposterOcta
    {
        static Vector2 SignNotZero(Vector2 v)
            => new Vector2(v.x >= 0f ? 1f : -1f, v.y >= 0f ? 1f : -1f);

        // ---- Full sphere (Y-pole) ----
        public static Vector2 FullEncode(Vector3 d)
        {
            d /= (Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z));
            Vector2 p = new Vector2(d.x, d.z);
            if (d.y < 0f)
            {
                Vector2 s = SignNotZero(p);
                p = new Vector2((1f - Mathf.Abs(p.y)) * s.x, (1f - Mathf.Abs(p.x)) * s.y);
            }
            return p * 0.5f + new Vector2(0.5f, 0.5f);
        }

        public static Vector3 FullDecode(Vector2 f)
        {
            f = f * 2f - Vector2.one;
            Vector3 d = new Vector3(f.x, 1f - Mathf.Abs(f.x) - Mathf.Abs(f.y), f.y);
            float t = Mathf.Clamp01(-d.y);
            d.x += d.x >= 0f ? -t : t;
            d.z += d.z >= 0f ? -t : t;
            return d.normalized;
        }

        // ---- Upper hemisphere (Y-pole) ----
        public static Vector2 HemiEncode(Vector3 d)
        {
            float denom = Mathf.Abs(d.x) + Mathf.Abs(d.z) + Mathf.Max(d.y, 1e-5f);
            Vector2 p = new Vector2(d.x, d.z) / denom;
            return new Vector2(p.x + p.y, p.x - p.y) * 0.5f + new Vector2(0.5f, 0.5f);
        }

        public static Vector3 HemiDecode(Vector2 f)
        {
            f = f * 2f - Vector2.one;
            Vector2 p = new Vector2(f.x + f.y, f.x - f.y) * 0.5f;
            float h = 1f - Mathf.Abs(p.x) - Mathf.Abs(p.y);
            return new Vector3(p.x, h, p.y).normalized;
        }

        public static Vector3 Decode(Vector2 uv, bool hemi)
            => hemi ? HemiDecode(uv) : FullDecode(uv);

        /// <summary>Direction captured by grid cell (x,y) in a frames×frames grid.</summary>
        public static Vector3 CellDirection(int x, int y, int frames, bool hemi)
        {
            float inv = 1f / (frames - 1);
            return Decode(new Vector2(x * inv, y * inv), hemi);
        }
    }
}
