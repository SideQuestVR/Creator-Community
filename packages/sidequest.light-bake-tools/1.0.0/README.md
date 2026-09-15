# SideQuest Light Bake Tools 1.0.0

Solves a lightmap resolution that fits an atlas budget, checks that the geometry you are
about to bake can actually hold baked light, and runs the bake in the background.

## What you receive

C# Editor scripts only. No runtime code, no components, no prefabs, no network access.

```
Assets/SideQuest/LightingTools/Core/   shared analysis
Assets/SideQuest/LightingTools/Bake/   this tool
```

Core is embedded so this package works on its own. If you also install another SideQuest
lighting tool, it carries the same Core files with the same GUIDs at the same paths, so the
second import overwrites rather than duplicates. **Keep all SideQuest lighting packages at
the same version.**

## Requirements

- Unity 6000.3.21f1, Universal Render Pipeline

## Use

`Tools > SideQuest > Lighting > Light Bake`

1. **Analyze Scene** — read the atlas estimate and the lightmap UV warnings first.
2. **Generate Missing Lightmap UVs** — only if analysis found models that need it.
3. **Accept Recommendations**
4. **Apply Plan** — writes a `LightingSettings` asset and assigns it. Starts no bake.
5. **Save the scene**, then **Start Bake**. **Bake Status** reports progress.
6. If you use reflection probes, **bake those afterwards** — they capture the scene as it is
   currently lit.

## The atlas budget

Lightmap resolution is entered as texels per world unit, which tells you nothing about the
number that actually constrains a world: how many atlas pages come out. A value that bakes
fine on a small scene quietly produces eleven 1024 pages on a large one, and nothing warns
you until the bake has finished.

Texels grow with surface area and with the square of resolution, so the resolution that
fits a given budget can be solved rather than found by bisection over hour-long bakes.
Analysis reports both the estimate and the fitting resolution. Over budget, Apply also
reduces Scale In Lightmap on the largest surfaces first, because texel density only needs
to come down where it is being spent.

Every bake compares its predicted texel count against what was actually produced and logs
the ratio. The estimator works from bounding-box area rather than real UV charts, so it can
only ever be approximately right — saying so after each bake is what keeps it honest.

## Lightmap UVs

A mesh with no second UV channel has nowhere to store baked light. Unity does not refuse
the bake; it bakes everything else and leaves that object wrongly lit, which is easy to
mistake for a lighting problem.

Analysis separates the fixable case from the unfixable one. An imported model has a
ModelImporter whose *Generate Lightmap UVs* can simply be switched on — that is what
**Generate Missing Lightmap UVs** does. A procedural or ProBuilder mesh has no importer at
all, and those are listed so you can decide.

That action edits asset importers, so it affects every scene using those models and is
**not** undoable with Ctrl+Z. It is kept as a separate step for that reason.

## Settings

Written to a `LightingSettings` asset at `Assets/Settings/SideQuestBakeSettings.lighting`
rather than into the scene's embedded settings, so it is diffable in version control and
shareable between scenes.

The preset targets standalone headsets: Progressive GPU, two bounces, ambient occlusion,
lightmap compression, and **non-directional** lightmaps — which halve lightmap memory, a
better trade than normal-mapped detail in baked light on a device bound by memory bandwidth.

## If Bakery is installed

Detected and left alone. This tool will not drive Bakery or change its settings; its API is
not a stable public contract and it is commercial, so no community package can depend on
it. The analysis half still applies — lightmap UVs, atlas budgeting and scale tuning all
feed Bakery too — and Apply leaves Unity's bake configuration untouched by default. Running
Unity's own bake alongside Bakery produces a second, competing set of lightmaps, and you
will be warned if you do.

## Removing it

Delete `Assets/SideQuest/LightingTools/`. Baked lightmaps, the lighting data asset and the
`LightingSettings` asset all remain as ordinary Unity assets. `Clear Baked Lighting` removes
the bake if you want to start over.

## Known limits

- URP only.
- The atlas estimate is approximate — bounding-box area, not UV charts. Treat it as "two
  pages or eleven", not as an exact figure.
- `Lightmapping.buildProgress` is unreliable for the Progressive lightmapper and frequently
  stays at zero. The status file reports it, but `running` and `elapsedSeconds` are the
  signals worth trusting.
- Bake settings are project-wide, not per-scene.

## Licence

MIT. See `LICENSE.txt`.
