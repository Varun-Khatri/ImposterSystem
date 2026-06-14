# VK Imposters — Octahedral Imposter Baker for URP / VR

A bake-time tool + runtime shader that replaces expensive meshes with an
octahedral billboard imposter: a single camera-facing quad that samples a
pre-baked atlas of the model captured from many angles, blending the 3 nearest
views so it never pops as the head moves.

## Why this design (VR-specific)

- **3-frame triangular blend**, not a single billboard. Single billboards flip
  and read flat in stereo; the blend hides angular transitions during head
  movement, which is the thing VR makes obvious.
- **All billboarding + frame selection is in the vertex shader.** No `Update`
  loop, no per-frame CPU work. State is pushed once via `MaterialPropertyBlock`.
- **Stereo-correct.** Frame selection uses the centered camera position so both
  eyes pick the *same* frames (no inter-eye seam). Per-eye parallax comes from
  the baked depth channel reprojected through each eye's matrices.
- **Alpha-to-coverage cutout.** MSAA gives soft silhouettes with no transparency
  sorting — cheap and order-independent, which matters at 2×90Hz.

## Files

```
Runtime/
  ImposterOcta.cs        CPU octahedral math (mirror of Imposter.hlsl)
  ImposterData.cs        ScriptableObject: atlases + metadata
  ImposterRenderer.cs    Quad + MPB setup, no Update loop
  VK.Imposters.asmdef
Editor/
  ImposterBakerWindow.cs Bakes a GameObject into an imposter (VK > Imposter Baker)
  VK.Imposters.Editor.asmdef
Shaders/
  Imposter.hlsl          Shared octa encode/decode + frame basis
  Imposter.shader        URP runtime: 3-frame blend, parallax, shadow caster
  ImposterCapture.shader  Bake-time capture (albedo+coverage / normal+depth)
```

## Usage

1. Open **VK > Imposter Baker**.
2. Drop in a prefab or scene GameObject (MeshRenderer or SkinnedMeshRenderer;
   skinned bakes the current pose).
3. Set frames-per-axis (12 is a good start = 144 views), tile resolution,
   hemisphere vs full sphere, and bake.
4. Output: an `_Imposter.asset`, an `_Imposter.mat`, and the atlas PNGs.
5. Add an empty GameObject, add **ImposterRenderer**, assign the `.asset` and the
   baked `.mat`. Scale the Transform to size it.

To use it as an LOD: drop the ImposterRenderer GameObject into an `LODGroup` as
the last LOD.

## Tuning knobs (the two you'll actually touch)

- **Frames per axis.** Higher = smoother angular blend, larger atlas. 12 hemi is
  usually enough for mid/background props; bump to 16 for hero objects.
- **Parallax Strength.** Depth-based silhouette correction. Start at ~0.25.
  If you see edge swimming or doubling in stereo, lower it; set to 0 (or untick
  Bake Normal+Depth) to disable entirely for the flattest/cheapest result.

## Known refinements (deliberately left out of v1)

- **Tile-edge bleeding:** mipmaps are disabled and UVs are clamped per tile, so
  bilinear can still fetch one neighbouring texel at a tile seam. If you see
  faint edges, add a half-texel inset in `FrameUV` / capture into padded tiles.
- **Capture lighting is baked in.** Albedo is captured under whatever lighting
  the scene has at bake time. For consistent results, bake under flat ambient
  (no directional light / neutral skybox), then enable `_IMPOSTER_LIT` at
  runtime to relight from the baked object-space normals.
- The math assumes the source mesh is roughly centered; the baker handles an
  off-center pivot via the computed bounding-sphere center, but very elongated
  meshes waste atlas space — consider per-object framing if that bites.

## Not tested in-engine

This was written without a Unity instance to run it against. The octa math,
frame blend, and bake pipeline are sound, but expect to tune parallax strength
and possibly the tile inset on first bake. The capture path covers URP
Lit/Simple Lit/Unlit material properties (`_BaseMap`/`_BaseColor`/`_BumpMap`,
plus legacy `_MainTex`/`_Color`); exotic custom shaders may need their base
texture property mapped in `BuildCaptureMaterials`.
