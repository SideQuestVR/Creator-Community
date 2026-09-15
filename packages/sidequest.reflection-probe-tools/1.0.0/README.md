# SideQuest Reflection Probe Tools 1.0.0

Finds the glossy surfaces in a scene and places reflection probes to serve them, within a
cubemap memory budget you set.

## What you receive

C# Editor scripts only. No runtime code, no components, no prefabs, no network access.

```
Assets/SideQuest/LightingTools/Core/              shared analysis
Assets/SideQuest/LightingTools/ReflectionProbes/  this tool
```

Core is embedded so this package works on its own. If you also install another SideQuest
lighting tool, it carries the same Core files with the same GUIDs at the same paths, so the
second import overwrites rather than duplicates. **Keep all SideQuest lighting packages at
the same version.**

## Requirements

- Unity 6000.3.21f1, Universal Render Pipeline

## Use

`Tools > SideQuest > Lighting > Reflection Probes`

1. **Analyze Scene** — read `problems[]` first, especially any shader it could not read.
2. **Accept Recommendations**
3. **Preview Plan** — boxes drawn in the Scene view, colour-coded by action.
4. **Apply Plan**
5. **Bake lightmaps**, then **Bake Probes**.

**That order matters.** A reflection probe captures the scene as it is currently lit, so a
probe baked before or alongside the lightmaps records lighting that no longer exists. Baking
again with identical settings then appears to fix it "for no reason". Analyze warns when a
probe's cubemap is older than the lightmaps.

## What it decides

- **Where** — glossy renderers are weighted by smoothness cubed, scaled by metallic and
  area, then clustered. Smoothness is cubed because perceived sharpness rises far faster
  than linearly. Capture points sit at eye height and are pushed clear of geometry: a probe
  inside a wall bakes to a black cubemap and nothing in the Editor tells you.
- **How sharp** — 32 to 256 px from smoothness and box size. A rough surface convolves
  detail away, so rendering 256 px to then blur it is waste.
- **How much** — roughly 1 MB per 128 px HDR probe. Over budget, resolutions come down
  least-glossy-first rather than probes being dropped; a slightly blurrier reflection
  everywhere beats a correct one in some rooms and the skybox in the rest.

Probes you placed by hand are never modified or deleted. The tool's own probes are named
`SQ_ReflectionProbe_*` and matched by a stable id, so re-analysing an unchanged scene
produces updates rather than deleting and recreating good cubemaps.

### Shaders it cannot read

A custom Shader Graph exposing no recognisable gloss property gets a neutral value at
quarter weight and is listed by name in the report — never a silent guess. Answer it once
in the window's **Shader gloss overrides** table and it applies everywhere.

Materials on URP **Simple Lit** or **Unlit** are reported separately: those shaders never
sample a reflection probe, so high smoothness there produces reflections that will never
appear, however many probes you place.

## Removing it

Delete `Assets/SideQuest/LightingTools/`. Probes already placed remain as ordinary
`ReflectionProbe` components. Baked cubemaps live under `Assets/ReflectionProbes/`.

## Known limits

- URP only.
- Reflection probe blending and box projection are settings on the URP asset. In a Banter
  world the **client's** URP asset decides them, so findings that depend on them are
  advisory and marked as such.
- Realtime probes are reported as unaffordable on standalone headsets rather than placed.
- Surface area is estimated from bounding boxes, not real geometry, so the memory estimate
  is approximate.

## Licence

MIT. See `LICENSE.txt`.
