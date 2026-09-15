# SideQuest Occlusion Culling Tools 1.0.0

Decides which geometry can hide other geometry and which should merely be hidden, then
derives Unity's three occlusion bake parameters from the scene instead of leaving them at
defaults.

## What you receive

C# Editor scripts only. No runtime code, no components, no prefabs, no network access.

```
Assets/SideQuest/LightingTools/Core/       shared analysis
Assets/SideQuest/LightingTools/Occlusion/  this tool
```

Core is embedded so this package works on its own. If you also install another SideQuest
lighting tool, it carries the same Core files with the same GUIDs at the same paths, so the
second import overwrites rather than duplicates. **Keep all SideQuest lighting packages at
the same version.**

## Requirements

- Unity 6000.3.21f1, Universal Render Pipeline

## Use

`Tools > SideQuest > Lighting > Occlusion`

1. **Analyze Scene** — check the occluder count. Zero means a bake would cull nothing.
2. **Accept Recommendations**
3. **Preview Plan** — Scene view tinted orange for occluders, blue for occludees, red for
   excluded.
4. **Apply Plan** — writes static flags and the bake parameters.
5. **Save the scene**, then **Start Bake**. The bake runs in the background; **Bake Status**
   reports progress and the resulting data size.

## What it decides

**Occluders should be a small set of large solid objects** — walls, floors, large
structural masses. Everything else is an occludee: it gets culled, but does not cull others.
Making too much of a scene occlude is the main way occlusion culling starts hiding things
the player is standing in front of, so the tool warns when most of a scene is occluding.

An occluder needs a substantial face, real thickness, and full opacity. Size is measured on
the second-largest bounds axis, not overall size: a wall is thin and is the best occluder
there is, while a long pipe is large and hides nothing. The threshold scales with ceiling
height, because 1.5 m is a wall in a corridor and a crate in a warehouse.

Excluded automatically: transparent and alpha-clipped materials, double-sided geometry,
paper-thin surfaces, and anything that can move. Baked occlusion records where geometry
*was*, so a movable object flagged either way culls against a memory of itself.

### The three bake parameters

- **Smallest Occluder** — the same threshold the classifier used, so the flagged set and
  the set Unity uses are the same set. Unity's default of 5 is sized for city blocks and
  finds nothing in a room-scale world.
- **Smallest Hole** — half the narrowest doorway the room segmentation measured, capped at
  Unity's 0.25 default. Raising this seals gaps and makes occluders more solid, and the
  space near any surface stops being described reliably — which looks like geometry
  vanishing when you stand close to it, not like a bake setting.
- **Backface Threshold** — left at 100. Lowering it shrinks the data but lets the camera
  see through walls from the wrong side, so it is reported rather than lowered for you.

Cost scales with scene volume over hole size cubed, so halving the hole size is eight times
the work. The estimate is checked before the bake rather than discovered hours into it.

### Tuning it yourself

If culling is too aggressive, raise **Occluder ceiling fraction** until only walls and
floors qualify, and lower **Smallest Hole**. Untick **Apply writes these** to keep values
you have tuned by hand; Apply will then change static flags only.

## Removing it

Delete `Assets/SideQuest/LightingTools/`. Static flags and baked occlusion data stay in the
scene — they are ordinary Unity data. `Clear Occlusion Data` removes the bake if you want
to start over.

## Known limits

- URP only.
- Verified by static flags and umbra data size, **not** by confirming in Play Mode that a
  hidden room's renderers report `isVisible == false`.
- `OcclusionPortal` placement is reported only, never automatic.
- Occlusion data lives in the scene, so a Banter world exported as a Kit carries none of it.

## Licence

MIT. See `LICENSE.txt`.
