# Burning Stick 0.1.0

An original closed low-poly stick with grabbable physics and independently
burning ends, adapted from Forest. By [BOBWORKS-XR](https://github.com/BOBWORKS-XR),
MIT. No third-party meshes, flame textures or audio are included.

## Requirements

Unity 6000.3.21f1, URP 17.3.0, Creator SDK 4.0.14 and Visual Scripting 1.9.1.
The Creator SDK must already be configured, including its Grabbable layer.
Unity 2022 and legacy Banter SDK compatibility have not been established.

## Add To Your Scene

1. Import this package. The shared flame is included; there is no separate
   flame download to find.
2. Choose **Tools > Creator Community > Add > Burning Stick**. The command adds
   one instance with the correct Grabbable layer and fresh object IDs. Position
   it, then save your scene when ready. The command does not save automatically.
3. Add **Portable Lighter** to provide an ignition source. The two packages are
   independently installable; the lighter is needed only for this demonstration.

Grab the stick around its middle. Each end lights when it touches a lit compatible
source. A burning stick can light another stick. Each end burns for **80 seconds**
of game time, and repeated contact does not reset that countdown. An expired end
can be relit while it touches a lit source; it is reusable rather than consumed.

## Customise And Integrate

Assets live under `Assets/CreatorCommunity/BurningStick`; graphs are in its
`VisualScripting` folder. Each `Portable_Ignition_Source_Stick_*` child has object
variables `BurnSeconds`, `Remaining`, `Lit`, `Tip`, `BodyObject` and `FlameVisual`.
Change BurnSeconds on each end to adjust its duration. Keep the source names,
Rigidbody-root event target and tip references when replacing the mesh.

Both enter and stay contacts are handled, so a source can ignite after it is
already overlapping a tip. Each tip checks the collider's distance from its own
position; a contact at the other end is not enough. Sources need the
`Portable_Ignition_Source` name prefix and a boolean `Lit` object variable.

No Forest tree registry, wildfire manager, scene coordinate or third-party
campfire package is shipped. This does not make arbitrary world objects burn.
Transform synchronisation uses the SDK; burn state is not advertised as a
network-synchronised effect. No custom C# runs at runtime.

## Testing And Removal

Editor graph checks cover unlit/distant/unconfigured source rejection, both ends,
repeat contact, burnout, relighting and stick-to-stick ignition. SDK node
validation and fresh package-import checks are recorded in
`dev/portable-props/REVIEW.md`. Native controller, hosted Quest and multiplayer
acceptance remain unverified. The preview shows the actual included prefab.

Remove scene instances before deleting BurningStick. Keep the shared
`Assets/CreatorCommunity/LighterFlame` folder while Portable Lighter uses it, and
the PortableProps/Editor menu while any of these prop packages are installed.
