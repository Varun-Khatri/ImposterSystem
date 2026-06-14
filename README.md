# VK Imposters

Octahedral imposter baker + runtime for **URP**, built for **VR**. Replaces an
expensive mesh with a single camera-facing quad that samples a pre-baked atlas
of the model captured from many angles, blending the 3 nearest views (with depth
parallax) so it stays convincing in stereo and never pops as the head moves.

## Requirements

- Unity 2022.3 LTS or newer
- Universal Render Pipeline (installed automatically as a dependency)
- Git installed on the machine (required for Package Manager git-URL import)

## Installation

**Package Manager → `+` → Add package from git URL:**

```
https://github.com/Varun-Khatri/ImposterSystem.git#1.0.0
```

Pin to a tag (`#1.0.0`) for reproducible imports; omit it to track the default
branch. Or add to `Packages/manifest.json`:

```json
"com.vk.imposters": "https://github.com/Varun-Khatri/ImposterSystem.git#1.0.0"
```

## Quick start

1. **Tool > VK > Imposter Baker**, drop in a prefab/GameObject, set frames (12 hemi is a
   good start), bake. You get an `_Imposter.asset`, a `.mat`, and atlas PNGs.
2. Add an empty GameObject, add **ImposterRenderer**, assign the `.asset` and the
   baked `.mat`, scale the Transform to size it.
3. Optionally drop it into an `LODGroup` as the final LOD.

## Tuning (VR)

- **Frames per axis** — higher = smoother angular blend, bigger atlas.
- **Parallax Strength** — depth silhouette correction; lower it if you see
  stereo edge-swimming, set 0 for the flattest/cheapest result.

See `Documentation~/index.md` for architecture notes and known refinements.

## License

MIT — see `LICENSE.md`.
