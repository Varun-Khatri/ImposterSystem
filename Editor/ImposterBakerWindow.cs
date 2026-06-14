using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VK.Imposters.Editor
{
    /// <summary>
    /// Bakes a GameObject (mesh or skinned, current pose) into an octahedral
    /// imposter atlas. Drawing is done through a CommandBuffer + DrawRenderer so
    /// it does not depend on URP's forward render loop or replacement shaders.
    /// </summary>
    public sealed class ImposterBakerWindow : EditorWindow
    {
        GameObject _source;
        int _frames = 12;
        int _tileResolution = 256;
        bool _hemi = true;
        bool _bakeNormalDepth = true;
        float _cutoff = 0.5f;
        string _outputFolder = "Assets/Imposters";

        [MenuItem("Tools/VK/Imposter Baker")]
        static void Open() => GetWindow<ImposterBakerWindow>("Imposter Baker");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _source = (GameObject)EditorGUILayout.ObjectField("Prefab / GameObject", _source, typeof(GameObject), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Capture", EditorStyles.boldLabel);
            _frames = EditorGUILayout.IntSlider("Frames Per Axis", _frames, 4, 32);
            _tileResolution = EditorGUILayout.IntPopup("Tile Resolution",
                _tileResolution, new[] { "128", "256", "512" }, new[] { 128, 256, 512 });
            _hemi = EditorGUILayout.Toggle("Hemisphere Only", _hemi);
            _bakeNormalDepth = EditorGUILayout.Toggle("Bake Normal + Depth", _bakeNormalDepth);
            _cutoff = EditorGUILayout.Slider("Coverage Cutoff", _cutoff, 0f, 1f);

            int atlas = _frames * _tileResolution;
            EditorGUILayout.HelpBox(
                $"{_frames * _frames} views → {atlas}×{atlas} atlas" +
                (_bakeNormalDepth ? " ×2 (albedo + normal/depth)" : ""),
                MessageType.Info);

            EditorGUILayout.Space();
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_source == null))
                if (GUILayout.Button("Bake", GUILayout.Height(32)))
                    Bake();
        }

        void Bake()
        {
            Directory.CreateDirectory(_outputFolder);

            // Instantiate at origin/identity so world space == local space.
            var instance = Instantiate(_source);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                var renderers = CollectRenderers(instance);
                if (renderers.Count == 0)
                {
                    Debug.LogError("[ImposterBaker] No renderers found on source.");
                    return;
                }

                ComputeBounds(renderers, out Vector3 center, out float radius);
                float dist = radius * 2f;
                float near = dist - radius;
                float range = 2f * radius;

                var cam = NewCaptureCamera(radius, dist);
                var captureMats = BuildCaptureMaterials(renderers, near, range);

                int atlasSize = _frames * _tileResolution;
                var albedoRT = NewRT(atlasSize, sRGB: true);
                RenderTexture normalRT = _bakeNormalDepth ? NewRT(atlasSize, sRGB: false) : null;

                RenderAtlas(albedoRT, renderers, captureMats, cam, center, dist, radius, pass: 0);
                if (_bakeNormalDepth)
                    RenderAtlas(normalRT, renderers, captureMats, cam, center, dist, radius, pass: 1);

                string baseName = _source.name;
                var albedoTex = SaveAtlas(albedoRT, $"{baseName}_Imposter_Albedo", sRGB: true);
                Texture2D normalTex = _bakeNormalDepth
                    ? SaveAtlas(normalRT, $"{baseName}_Imposter_NormalDepth", sRGB: false)
                    : null;

                CreateAsset(baseName, albedoTex, normalTex, radius, center);

                // cleanup
                DestroyImmediate(cam.gameObject);
                albedoRT.Release();
                DestroyImmediate(albedoRT);
                if (normalRT != null)
                {
                    normalRT.Release();
                    DestroyImmediate(normalRT);
                }

                foreach (var m in captureMats.Values) DestroyImmediate(m);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[ImposterBaker] Baked '{baseName}' → {_outputFolder}");
            }
            finally
            {
                DestroyImmediate(instance);
            }
        }

        // ---------------------------------------------------------------------

        static List<Renderer> CollectRenderers(GameObject root)
        {
            var list = new List<Renderer>();
            list.AddRange(root.GetComponentsInChildren<MeshRenderer>(false));
            list.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(false));
            return list;
        }

        static void ComputeBounds(List<Renderer> renderers, out Vector3 center, out float radius)
        {
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++) b.Encapsulate(renderers[i].bounds);
            center = b.center;
            radius = b.extents.magnitude;
        }

        static Camera NewCaptureCamera(float radius, float dist)
        {
            var go = new GameObject("ImposterCaptureCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = radius;
            cam.nearClipPlane = Mathf.Max(0.001f, dist - radius * 1.5f);
            cam.farClipPlane = dist + radius * 1.5f;
            cam.enabled = false; // we never call Render(); only read matrices
            return cam;
        }

        Dictionary<Material, Material> BuildCaptureMaterials(List<Renderer> renderers, float near, float range)
        {
            var shader = Shader.Find("Hidden/VK/ImposterCapture");
            var map = new Dictionary<Material, Material>();
            foreach (var r in renderers)
            foreach (var src in r.sharedMaterials)
            {
                if (src == null || map.ContainsKey(src)) continue;
                var cap = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                CopyProp(src, cap, "_BaseMap");
                CopyProp(src, cap, "_MainTex", "_BaseMap");
                CopyColor(src, cap, "_BaseColor");
                CopyColor(src, cap, "_Color", "_BaseColor");
                CopyProp(src, cap, "_BumpMap");
                cap.SetFloat("_Cutoff", src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : _cutoff);
                cap.SetFloat("_CaptureNear", near);
                cap.SetFloat("_CaptureRange", range);
                map[src] = cap;
            }

            return map;
        }

        static void CopyProp(Material src, Material dst, string name, string dstName = null)
        {
            dstName ??= name;
            if (src.HasProperty(name) && src.GetTexture(name) != null)
            {
                dst.SetTexture(dstName, src.GetTexture(name));
                dst.SetTextureScale(dstName, src.GetTextureScale(name));
                dst.SetTextureOffset(dstName, src.GetTextureOffset(name));
            }
        }

        static void CopyColor(Material src, Material dst, string name, string dstName = null)
        {
            dstName ??= name;
            if (src.HasProperty(name)) dst.SetColor(dstName, src.GetColor(name));
        }

        static RenderTexture NewRT(int size, bool sRGB)
        {
            var rt = new RenderTexture(size, size, 24,
                    sRGB ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGB32,
                    sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
                { useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear };
            rt.Create();
            return rt;
        }

        void RenderAtlas(RenderTexture target, List<Renderer> renderers,
            Dictionary<Material, Material> captureMats, Camera cam,
            Vector3 center, float dist, float radius, int pass)
        {
            var cmd = new CommandBuffer { name = "ImposterBake" };
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));

            for (int y = 0; y < _frames; y++)
            for (int x = 0; x < _frames; x++)
            {
                Vector3 dir = ImposterOcta.CellDirection(x, y, _frames, _hemi);
                Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                cam.transform.position = center + dir * dist;
                cam.transform.rotation = Quaternion.LookRotation(-dir, up);

                cmd.SetViewport(new Rect(x * _tileResolution, y * _tileResolution, _tileResolution, _tileResolution));
                cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);

                foreach (var r in renderers)
                {
                    var mats = r.sharedMaterials;
                    for (int sub = 0; sub < mats.Length; sub++)
                    {
                        if (mats[sub] == null || !captureMats.TryGetValue(mats[sub], out var cap)) continue;
                        cmd.DrawRenderer(r, cap, sub, pass);
                    }
                }
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            GL.Flush();
        }

        Texture2D SaveAtlas(RenderTexture rt, string fileName, bool sRGB)
        {
            int size = rt.width;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !sRGB);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string path = Path.Combine(_outputFolder, fileName + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.alphaIsTransparency = sRGB;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        void CreateAsset(string baseName, Texture2D albedo, Texture2D normalDepth, float radius, Vector3 center)
        {
            var data = ScriptableObject.CreateInstance<ImposterData>();
            data.albedoAtlas = albedo;
            data.normalDepthAtlas = normalDepth;
            data.frames = _frames;
            data.hemiOctahedron = _hemi;
            data.radius = radius;
            data.pivot = center;
            AssetDatabase.CreateAsset(data, Path.Combine(_outputFolder, baseName + "_Imposter.asset"));

            var mat = new Material(Shader.Find("VK/Imposter")) { name = baseName + "_Imposter" };
            mat.SetTexture("_AlbedoAtlas", albedo);
            if (normalDepth != null) mat.SetTexture("_NormalDepthAtlas", normalDepth);
            mat.SetFloat("_Frames", _frames);
            mat.SetFloat("_ImposterSize", radius * 2f);
            mat.SetVector("_Pivot", center);
            mat.SetFloat("_Cutoff", _cutoff);
            if (_hemi) mat.EnableKeyword("_IMPOSTER_HEMI");
            if (normalDepth != null) mat.EnableKeyword("_IMPOSTER_PARALLAX");
            AssetDatabase.CreateAsset(mat, Path.Combine(_outputFolder, baseName + "_Imposter.mat"));
        }
    }
}