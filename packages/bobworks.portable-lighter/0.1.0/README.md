# Portable Lighter 0.1.0

An original primitive-built, always-open lighter with the Forest flame visual:
blue base, warm tip, subtle flicker and movement lag. No third-party lighter model
or textures are included. By [BOBWORKS-XR](https://github.com/BOBWORKS-XR), MIT.

## Requirements

- Unity 6000.3.21f1, URP 17.3.0, Creator SDK 4.0.14 and Visual Scripting 1.9.1.
- Creator SDK configured, including its `Grabbable` layer and Visual Scripting.
- This is not a Unity 2022 / legacy Banter SDK compatibility release.

## Add To Your Scene

1. Import the package through Creator Plugins or Unity's package importer.
2. Choose **Tools > Creator Community > Add > Portable Lighter**. This creates
   one prefab instance, assigns your project's Grabbable layer and fresh object
   IDs, and selects it. It does not save the scene or change project settings.
3. Position the instance and save your scene when ready.
4. Import **Burning Stick** separately to try the matching ignition interaction.

Grab with either hand. Squeeze and release the held hand's trigger to toggle the
flame. Holding the trigger does not repeatedly toggle it. Dropping the lighter
extinguishes it. Touch the flame sensor to a stick end to light it.

## Contents And Customisation

Files live under `Assets/CreatorCommunity/PortableLighter`, with the shared flame
under `Assets/CreatorCommunity/LighterFlame` and the explicit insertion menu under
`Assets/CreatorCommunity/PortableProps/Editor`. Do not move Editor code into a
runtime folder. The interaction graphs are editable in `VisualScripting`.

The grip, Rigidbody, trigger sensor and object variables are part of the prefab.
Keep those when replacing visuals. `Portable_Ignition_Source_Lighter` exposes
the `Lit` object variable used by the matching stick. The click is original
procedural audio. No custom C# is required at runtime.

## Scope And Testing

This is a portable prop, not the entire Forest wildfire system. It lights the
compatible Burning Stick; it does not automatically make arbitrary trees or
objects burn. Scene-specific tree registries, fire managers and Forest audio
are deliberately excluded. Grabbable transform synchronisation uses the SDK;
the flame state is not advertised as a network-synchronised effect.

Windows Editor graph checks cover both hands, trigger hysteresis, repeated
presses, unheld/wrong-hand rejection, release, independent prefab references and
stick contact. Creator SDK node validation is required before publication.
Quest/controller feel, hosted native execution and multiplayer are not yet
verified. The original Forest project is not modified by this package.

The preview is rendered from the actual included prefab, not stock imagery.
See the repository's `dev/portable-props/REVIEW.md` for exact release evidence.

## Removal

Remove scene instances first, then delete the PortableLighter folder. Keep the
shared LighterFlame folder while Burning Stick uses it. Keep PortableProps/Editor
while any of these prop packages use its menu. This removes no unrelated assets.
