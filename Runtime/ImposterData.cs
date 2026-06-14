using UnityEngine;

namespace VK.Imposters
{
    /// <summary>
    /// Baked imposter description. Produced by the baker, consumed by
    /// ImposterRenderer. Holds the two atlases plus the geometry metadata the
    /// runtime needs to reconstruct sampling.
    /// </summary>
    [CreateAssetMenu(menuName = "VK/Imposter Data", fileName = "ImposterData")]
    public sealed class ImposterData : ScriptableObject
    {
        [Tooltip("RGB = baked albedo, A = coverage mask.")]
        public Texture2D albedoAtlas;

        [Tooltip("RGB = object-space normal (0..1 encoded), A = linear depth (0 = near silhouette, 1 = far). Null if not baked.")]
        public Texture2D normalDepthAtlas;

        [Tooltip("Frames per axis. Total captured views = frames * frames.")]
        public int frames = 12;

        [Tooltip("Upper hemisphere only (good for ground props). Off = full sphere.")]
        public bool hemiOctahedron = true;

        [Tooltip("Radius of the source mesh's bounding sphere, in the mesh's local space.")]
        public float radius = 0.5f;

        [Tooltip("Pivot offset of the bounding sphere from the mesh origin, local space.")]
        public Vector3 pivot = Vector3.zero;

        public bool HasNormalDepth => normalDepthAtlas != null;
    }
}
