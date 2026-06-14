using UnityEngine;
using UnityEngine.Rendering;

namespace VK.Imposters
{
    /// <summary>
    /// Drives a single imposter quad. All orientation/frame work happens in the
    /// vertex shader, so this component does no per-frame work — it just builds
    /// the quad mesh once and pushes the baked atlas + metadata via a
    /// MaterialPropertyBlock. Sizing comes from the Transform scale.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ImposterRenderer : MonoBehaviour
    {
        [SerializeField] ImposterData _data;
        [SerializeField] Material _material;          // a material using "VK/Imposter"
        [SerializeField, Range(0,1)] float _cutoff = 0.5f;
        [SerializeField, Range(0,1)] float _parallax = 0.25f;
        [SerializeField] bool _receiveMainLight = false;

        static readonly int IdAlbedo   = Shader.PropertyToID("_AlbedoAtlas");
        static readonly int IdNormal   = Shader.PropertyToID("_NormalDepthAtlas");
        static readonly int IdFrames   = Shader.PropertyToID("_Frames");
        static readonly int IdSize     = Shader.PropertyToID("_ImposterSize");
        static readonly int IdPivot    = Shader.PropertyToID("_Pivot");
        static readonly int IdCutoff   = Shader.PropertyToID("_Cutoff");
        static readonly int IdParallax = Shader.PropertyToID("_Parallax");

        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Mesh _quad;

        public ImposterData Data
        {
            get => _data;
            set { _data = value; Apply(); }
        }

        void OnEnable() => Apply();
        void OnValidate() { if (isActiveAndEnabled) Apply(); }
        void OnDestroy() { if (_quad != null) DestroyImmediate(_quad); }

        public void Apply()
        {
            if (_data == null || _material == null) return;

            var mf = GetComponent<MeshFilter>();
            if (_quad == null) _quad = BuildQuad();
            // Culling bounds must cover the billboard sphere (quad corners reach
            // radius*sqrt2 past the pivot), or imposters pop out at frame edges.
            _quad.bounds = new Bounds(_data.pivot, Vector3.one * (_data.radius * 3f));
            mf.sharedMesh = _quad;

            _renderer ??= GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;

            // Keyword state lives on the material instance the user wired up,
            // but hemi/lit are baked-in choices, so enforce them here too.
            _material.SetKeyword("_IMPOSTER_HEMI", _data.hemiOctahedron);
            _material.SetKeyword("_IMPOSTER_LIT", _receiveMainLight && _data.HasNormalDepth);
            _material.SetKeyword("_IMPOSTER_PARALLAX", _data.HasNormalDepth && _parallax > 0f);

            _mpb ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture(IdAlbedo, _data.albedoAtlas);
            if (_data.HasNormalDepth) _mpb.SetTexture(IdNormal, _data.normalDepthAtlas);
            _mpb.SetFloat(IdFrames, _data.frames);
            _mpb.SetFloat(IdSize, _data.radius * 2f);
            _mpb.SetVector(IdPivot, _data.pivot);
            _mpb.SetFloat(IdCutoff, _cutoff);
            _mpb.SetFloat(IdParallax, _parallax);
            _renderer.SetPropertyBlock(_mpb);
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "VK_ImposterQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3( 0.5f, -0.5f, 0),
                new Vector3( 0.5f,  0.5f, 0),
                new Vector3(-0.5f,  0.5f, 0),
            };
            m.uv = new[]
            {
                new Vector2(0,0), new Vector2(1,0), new Vector2(1,1), new Vector2(0,1),
            };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            return m;
        }
    }

    static class MaterialKeywordExt
    {
        public static void SetKeyword(this Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }
    }
}
