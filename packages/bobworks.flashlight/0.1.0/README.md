# Flashlight 0.1.0

An original primitive-built watchtower torch, based on Forest's interaction setup.
By [BOBWORKS-XR](https://github.com/BOBWORKS-XR), MIT. No third-party flashlight
mesh, textures, audio, rack or world scene is included.

## Requirements

Unity 6000.3.21f1, Creator SDK 4.0.14, URP 17.3.0 and Visual Scripting 1.9.1.
Configure the SDK and its Grabbable layer first. Legacy Banter/Unity 2022 and
other render pipelines have not been verified.

## Add And Use

1. Import this package through Creator Plugins or Unity's package importer.
2. Choose **Tools > Creator Community > Add > Flashlight**. This adds an instance
   with fresh object IDs and the current project's grab layer. It does not save
   your scene or change project settings.
3. Position it at its starting place and save the scene yourself. The lens points
   along the prefab's local positive Y axis.
4. Grab its handle with either hand and press/release the trigger to toggle the
   beam. The SDK trigger is single-shot, not automatic repeat.

Like Forest's mounted torches, it starts with the light off and gravity disabled.
The first grab enables gravity. Releasing it leaves its light state unchanged;
it does not automatically switch off or return to a rack. For a torch that should
fall immediately on world startup, enable Use Gravity on the root Rigidbody.

## Customise

The Spot child controls the beam: 16 m range, 48-degree outer angle, 30-degree
inner angle, cool-white colour and no shadows. Enable additional per-pixel lights
in your world's/client's URP configuration if the beam does not render. A package
cannot override the running client's lighting budget or configuration.

The native graph is under `Assets/CreatorCommunity/Flashlight/VisualScripting`.
Its object variables are IsOn, Grip, Body, Sync, Spot and LensGlow. Keep their
references when changing the visual model. The physical body is on Default;
only Grab_Handle uses the project's Grabbable layer.

## Scope And Testing

Runtime logic uses Creator SDK components and Visual Scripting, not custom
runtime C#. The included C# is an explicit Editor insertion menu, separate from
the other props' menu so import order cannot remove its entry.

Windows Editor checks cover graph connections, source collider/light settings,
both-hand grab/toggle paths, independent instances, SDK allow-list validation,
actual spotlight on/off rendering and package insertion. Native ownership calls
are bypassed only in cloned test graphs. Native controller feel, hosted Quest,
multiplayer and other operating systems remain unverified. Transform sync is
configured; the light toggle is local, not advertised as network-synchronised.

The preview is rendered from the actual prefab. Maintainer evidence is in
`dev/portable-props/FLASHLIGHT-REVIEW.md` in the catalogue repository.

## Removal

Remove scene instances, then delete `Assets/CreatorCommunity/Flashlight`.
No shared menu or asset from the other prop packages needs to be removed.
