# Blam Radio 0.1.0

A portable version of the Blam radio, rebuilt with original boxes, speaker
cylinders, a tuning dial, antenna and a horizontal carry handle with a triangular cross-section.
By [BOBWORKS-XR](https://github.com/BOBWORKS-XR), MIT.

**Music: "Climb on up (v2)" by BOBWORKS-XR.** The author explicitly approved MIT
distribution of this track with GitHub credit on 2026-09-15. The package contains
the original MP3 bytes as `Audio/BlamTrack.mp3`, not a generated replacement.

## Requirements

Unity 6000.3.21f1, URP 17.3.0, Creator SDK 4.0.14 and Visual Scripting 1.9.1,
with the Creator SDK and its Grabbable layer already configured. Although based
on Blam's older Banter radio, this export targets the current Creator SDK. Legacy
Banter/Unity 2022 compatibility is not claimed.

## Add And Use

1. Import the package through Creator Plugins or Unity's package importer.
2. Choose **Tools > Creator Community > Add > Blam Radio**. This adds a prefab,
   assigns the project's Grabbable layer and fresh IDs, and selects it.
3. Place it where it should live. Save the scene when ready; the menu never saves
   automatically or changes your project settings.

The radio plays the included track on a spatial loop and can be picked up by its
top handle. It returns to the position and rotation captured when the world starts
after **five minutes without being held**, or when it enters a trigger tagged
`Finish`. Only the owning client may perform that return. Returning clears velocity.
There are no hardcoded Blam world coordinates.

Select the `Audio` child to change the clip, volume or distance falloff. Use music
you have permission to redistribute with your world. `ReturnAfterSeconds` in the
root object's Variables component changes the idle timeout. The native graph is
editable under `Assets/CreatorCommunity/BlamRadio/VisualScripting`.

## Scope And Testing

The original paid radio model, material textures and Blam scene are not included.
All replacement geometry and materials are original. No custom runtime C# is
shipped; the only C# in the package is an explicit Editor insertion menu.

Editor tests check placement capture, five-minute idle return, held/non-owner
rejection, Finish-trigger return, velocity reset and the configured music loop.
Ownership is stubbed only in test-clone graphs, not in the shipped prefab.
Controller feel, native networking, hosted Quest and multiplayer audio timing
remain unverified. Playback is local; there is no synchronised music clock.

The preview is rendered from this prefab. Exact test evidence is in
`dev/portable-props/REVIEW.md` in the catalogue repository.

## Removal

Remove scene instances, then delete `Assets/CreatorCommunity/BlamRadio`. Keep
`Assets/CreatorCommunity/PortableProps/Editor` while another portable prop uses
its insertion menu. No scene, user music library or unrelated package is deleted.
