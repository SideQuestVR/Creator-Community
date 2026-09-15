# SideQuest Lighting Core 1.0.0

Shared scene analysis for the SideQuest lighting tools. On its own it reads a scene and
writes a report; it never changes anything.

The four tool packages — light probes, reflection probes, occlusion culling, light baking
— each embed this Core. **If you are installing one of those, you do not need this package
as well.** Install it on its own only if you want the scene analysis without any of the
tools.

## What you receive

C# Editor scripts only. No runtime code, no components, no prefabs, no shaders, no
network access, no files written outside your project.

```
Assets/SideQuest/LightingTools/Core/
```

One assembly, `SideQuest.LightingTools.Core.Editor`, Editor-only.

## Requirements

- Unity 6000.3.21f1 (see the listing for what was actually tested)
- Universal Render Pipeline. The tools are URP-only and refuse to run otherwise rather
  than producing advice derived from URP material properties that do not apply.

## What it does

One pass over the scene produces:

- Renderer facts — bounds, triangle counts, lightmap UVs, static flags, whether an object
  can move.
- Material facts — smoothness, metallic, surface type, and whether the shader samples
  reflection probes at all. A custom Shader Graph with no readable gloss gets a neutral
  value at reduced weight and is listed by name, never a silent guess.
- URP pipeline facts, each marked advisory, because in a Banter world the client's URP
  asset decides them rather than yours.
- Rooms. Free space is voxelised from colliders, the outside is excluded, and what remains
  is eroded to close doorways so the surviving cores are the rooms. Doorway widths fall out
  of this and are what occlusion culling needs.

Reports are written to `<ProjectRoot>/SideQuestLighting/`, outside `Assets/`, so they
never end up in a built AssetBundle. Add that folder to your `.gitignore`.

## Removing it

Delete `Assets/SideQuest/LightingTools/Core/`. Nothing else is touched. If you have any of
the tool packages installed, they need Core — remove those first or they will not compile.

## Known limits

- URP only.
- Room detection reads colliders. A scene with no colliders falls back to renderer bounding
  boxes, which reports concave rooms as filled boxes; this is stated in the report rather
  than applied silently.
- Scene analysis is not a lighting simulation. Density and weighting heuristics are
  deliberately cheap so they can run while you move a slider.

## Licence

MIT. See `LICENSE.txt`.
