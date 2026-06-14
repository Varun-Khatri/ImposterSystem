# Changelog

All notable changes to this package are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/);
versions follow [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-10

### Added
- Octahedral imposter baker (`VK > Imposter Baker`) supporting MeshRenderer and
  SkinnedMeshRenderer sources, hemisphere or full-sphere capture, and an
  optional normal+depth atlas.
- URP runtime shader with vertex-shader billboarding, 3-frame triangular blend,
  depth-based parallax, alpha-to-coverage cutout, and a shadow caster pass.
- `ImposterRenderer` component (no per-frame CPU work; MaterialPropertyBlock
  driven) and `ImposterData` ScriptableObject.
- Stereo single-pass-instanced support for VR.
