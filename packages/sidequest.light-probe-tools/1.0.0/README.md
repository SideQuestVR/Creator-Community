# SideQuest Light Probe Tools 1.0.0

Places light probes, and decides which objects should own lightmap texels versus read their
lighting from the probes instead.

## What you receive

C# Editor scripts only. No runtime code, no components, no prefabs, no network access.

```
Assets/SideQuest/LightingTools/Core/          shared analysis
Assets/SideQuest/LightingTools/LightProbes/   this tool
```

Core is embedded here on purpose, so this package works on its own. If you also install
another SideQuest lighting tool it carries the same Core files with the same GUIDs at the
same paths, so the second import overwrites rather than duplicates. **Keep all SideQuest
lighting packages at the same version** — mixing versions is the one case where that
overwrite is not what you want.

## Requirements

- Unity 6000.3.21f1, Universal Render Pipeline
- A NavMesh, only if you want the NavMesh placement strategy

## Use

`Tools > SideQuest > Lighting > Light Probes`

1. **Analyze Scene** — writes a report and a recommended plan.
2. **Accept Recommendations** — copies that plan into an editable file.
3. **Preview Plan** — draws it in the Scene view. Nothing is modified.
4. **Apply Plan** — places the probes. One Ctrl+Z reverts everything.

Then bake lighting as usual. The plan is plain JSON under
`<ProjectRoot>/SideQuestLighting/plans/` — edit it by hand, or let an agent adjust it.

### Three placement strategies

- **Adaptive** — density from lights, geometry edges and how sharply lighting varies.
  Denser at shadow boundaries and lamp falloff, sparser across uniform floors. Needs
  nothing but the scene.
- **NavMesh / Mesh volume** — a grid over walkable space plus its boundary edges, or a fill
  inside a collider you choose. Use when you know exactly which space matters.
- **Agent / manual** — positions you paint in the Scene view or supply in the plan, for
  judgement the heuristics cannot encode.

Probes are placed in vertical layers rather than one flat sheet. A single horizontal set
lights the floor and not the viewer's head, and tetrahedralises into slivers that
interpolate badly in every direction.

### Contribute GI

Applying also sets Contribute GI: objects too small to bounce useful light stop
contributing, small objects keep contributing but read their own shading from probes rather
than taking lightmap space, and anything that can move is excluded — a moving object baked
into a lightmap leaves its shadow behind.

## Removing it

Delete `Assets/SideQuest/LightingTools/`. Probes already placed remain in your scene; they
are ordinary `LightProbeGroup` data with no dependency on this package. The tool's own group
is named `SQ_LightProbes`.

## Known limits

- URP only.
- **Adaptive Probe Volumes are not supported.** If your URP asset uses APV, a classic Light
  Probe Group is ignored at render time, so the tool refuses to place one and says so.
- Probes are placed in enclosed space where a scene has any. A world that is partly indoors
  and partly open gets probes indoors only — use the NavMesh or Agent strategy for outdoor
  areas.
- Spacing means metres everywhere. If you used an older script where a "density" value did
  the opposite, this will feel inverted; it is not.

## Licence

MIT. See `LICENSE.txt`.
