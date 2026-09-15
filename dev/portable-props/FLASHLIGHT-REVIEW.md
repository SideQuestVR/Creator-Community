# Flashlight 0.1.0 Review

2026-09-15. Original replacement geometry and native Visual Scripting by
[BOBWORKS-XR](https://github.com/BOBWORKS-XR), MIT.

## Scope

The interaction reference was Forest's LookoutFlashlightRackPass.cs and its
watchtower flashlights. Its referenced PolyUniversal FBX and texture are not
redistributed. The replacement uses Unity cylinders/cubes and original materials;
there is no rack, Forest scene, third-party mesh or audio in the download.

The original configuration is retained: 0.36 kg Rigidbody, initial gravity off,
gravity enabled on grab, cylinder grip at (0, -0.045, 0), collider radius 0.052 /
height 0.19, native grab radius 0.05, single-shot trigger sensitivity 0.45 and
fire rate 0.12. The beam has range 16, outer/inner angles 48/30, intensity 1.45,
cool-white colour, no shadows and the source's bounding-sphere override.
Start disables beam/glow; trigger toggles both; release does not extinguish it.
Native ownership requests remain on grab and toggle. Only the grip uses Grabbable;
the physical body remains on Default.

## Verified

- Unity 6000.3.21f1, Creator SDK 4.0.14, URP 17.3.0, VS 1.9.1, Windows.
- 27 graph/configuration checks passed before export and after real package
  import/script reload into a disposable SDK project containing the other props.
- Eight placement checks passed: separate menu compiles, fixture layer valid,
  two instances have four unique IDs, grip-only layer mapping, no automatic scene
  save, Undo, and IDs/layers persist after explicit save/reopen.
- The 68 existing lighter/stick/radio checks passed alongside the imported torch.
- A rendered off/on comparison illuminated 33,242 pixels, with mean luminance
  increase 0.0419080248721487. The included spotlight, not an added test light,
  illuminated the target. The catalogue preview is rendered from the prefab.
- SDK allow-list validation passed. No graph-check errors were logged.
- Export dependencies are restricted to Assets/CreatorCommunity/Flashlight.
  The only C# shipped is CreatorFlashlightMenu.cs in that folder's Editor child.
  Its separate class/path cannot overwrite the older props' insertion menu.
- Archive metadata, GUIDs, package size/SHA-256, license and README were checked.
- The Forest source scene hash was unchanged across this pass. No source project
  was opened, saved or otherwise modified. Hub/MCP/Setup source is untouched.

Compact receipts are in evidence/flashlight/. The package is independently
installable; the other props are regression-test companions, not dependencies.

## Limits

This proves Editor graph paths, configuration, package integration and rendering,
not physical controller feel, native network ownership, hosted Quest, multiplayer,
other operating systems or legacy Banter/Unity 2022 compatibility. Ownership
requests are bypassed only in cloned test graphs. Native SDK input dispatch and
single-shot controller timing are not simulated by injecting trigger events.
Transform sync is configured; the light state is local. Client URP configuration
and additional-light budgets can affect visibility. No malware-certification
claim is made.

## Reproduce

Use a disposable SDK project with a COMMUNITY_PROP_FIXTURE marker at its root.
Copy the existing PortablePropGraphs.cs, PortablePropsBuilder.cs and
PortablePropsTests.cs plus PortableFlashlight.cs into Assets/Editor.
The partial builder reuses those existing development helpers; they are not
shipped runtime dependencies. Put the README/license in
Assets/CreatorCommunity/Flashlight and CreatorFlashlightMenu.cs in its Editor
subfolder. Run PortablePropsBuilder.BuildFlashlightPackage with -batchmode and
-quit to build, test, capture and export.

To validate the actual archive alongside the existing three props, put it in
sibling CommunityProps/Exports and run FlashlightImportSmoke.Run in a disposable
CommunityPropsImport project. Omit -quit: it waits for import completion and script
reload, runs the tests and exits itself. The placement test temporarily changes
UserLayer12's name and restores it in finally. It saves only its own fixture scene.
