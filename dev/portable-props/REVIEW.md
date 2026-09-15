# Portable Props Maintainer Review

Date: 2026-09-15. Packages: Portable Lighter, Burning Stick and Blam Radio 0.1.0.
Author: [BOBWORKS-XR](https://github.com/BOBWORKS-XR). MIT.

## Provenance And Scope

- The lighter casing, stick, radio and their materials are original generated
  geometry. No paid lighter/radio model, texture or source-world scene is shipped.
- The shared flame is an original skinned mesh, shader and native Visual Scripting
  movement graph. It has no third-party image dependency.
- The radio contains the author's original "Climb on up (v2)" MP3 bytes. The
  author explicitly approved MIT redistribution with BOBWORKS-XR GitHub credit.
  A new asset GUID prevents replacing an existing copy in the source project.
- The radio's horizontal grab reference is the original collider: local
  position (0, 0.438, 0), Z rotation 90 degrees, capsule Y axis, height 0.58,
  radius 0.02, non-trigger, cylinder grab radius 0.02. Its replacement handle
  is a horizontal triangular-section bar with outward-facing triangles.
- Forest and Blam are read-only source references for this packaging pass.
  No source scene, third-party asset or installed app is changed by these packages.

## Verified Checks

Test environment: Windows, Unity 6000.3.21f1, Creator SDK 4.0.14,
URP 17.3.0 and Visual Scripting 1.9.1, in disposable projects.

Final exported archives: **68 graph/geometry checks and seven placement checks
passed** after importing the corrected handle. The initial empty-project import
and the subsequent archive-update import both completed. Compact receipts and
the exact archive inventory/hashes are in `evidence/` beside this review.

- Native graph execution checks cover both lighter hands, trigger hysteresis,
  repeated presses, wrong-hand/unheld rejection and extinguishing on release.
- Stick checks cover both ends, unlit/distant/unconfigured source rejection,
  contact after overlap, repeated-contact timer preservation, independent burnout,
  relighting with a fresh timer and stick-to-stick ignition.
- Radio checks cover captured scene placement, five-minute return, held/non-owner
  rejection, Finish-trigger return, velocity reset, track playback configuration,
  original collider geometry and outward handle triangle winding.
- Missing scripts, graph connections and Creator SDK node allow-list are checked.
  Ownership is replaced with a literal only in a test-clone graph; the package
  keeps the native ownership call.
- A separate Play Mode test used actual Unity trigger contact and the shipped
  stick graph's Update event, not injected events. It ignited and extinguished
  after 79.95754 game seconds with an 80-second setting. The source was moved away
  after ignition. This isolated test does not exercise native VR controllers.
- All three real exported archives imported in an initially empty SDK project,
  waiting for each importPackageCompleted callback and script reload.
- Two of each imported prefab were inserted with the shipped menu. Six instances
  kept unique IDs and a deliberately different project Grabbable layer after
  explicit scene save/reopen. The menu itself did not save the scene.
- Archive checks cover scoped paths, metadata GUIDs, identical shared assets,
  author-owned music byte equality, README/license inclusion and download hashes.
- All three 960 x 540 previews are rendered from the actual included prefabs,
  not stock art. The handle preview was regenerated after correcting its normals.

The first fresh SDK import emitted an Ora assembly-updater warning about
UnityEngine.UI. The subsequent prop compilation and graph checks passed;
this is not a claim that SDK bootstrap emitted no warnings.

## Verification Boundaries

These are initial portable Creator SDK releases, not verified legacy Banter or
Unity 2022 exports. Native controller feel, hosted Quest/Altspace execution,
multiplayer, other operating systems and actual network ownership remain untested.
The SDK transform-sync components are configured; flame/burn state and music
playback are local rather than a network-synchronised effect or shared clock.
The packages do not include Forest's wildfire manager or make arbitrary trees burn.
No malicious-content certification or antivirus guarantee is implied.

## Reproduce In A Disposable Project

1. Configure the pinned Unity/Creator SDK/URP/Visual Scripting versions and
   Grabbable layer. Create the file COMMUNITY_PROP_FIXTURE at the project root.
   Never run these development tools in a user world.
2. Import the three versioned archives. For the automated import runner, use
   sibling directories CommunityProps/Exports and CommunityPropsImport;
   copy PortablePropsImportSmoke.cs to the import project's Assets/Editor.
   Run PortablePropsImportSmoke.Run without -quit: it waits and exits itself.
3. Copy PortablePropGraphs.cs, PortablePropsBuilder.cs and PortablePropsTests.cs
   to Assets/Editor. Run PortablePropsBuilder.Test; it tests the imported assets,
   not regenerated replacements. The receipt is props-tests-passed.txt.
4. Copy PortablePropsPlacementSmoke.cs and run PortablePropsPlacementSmoke.Run
   to test insertion and persistence. This temporarily uses UserLayer12 and
   restores the original layer names in finally. Receipt: placement-passed.txt.
5. Copy PortableStickPlaySmoke.cs and run PortableStickPlaySmoke.Run without
   -quit for the actual 80-second physics/Update test. It exits itself.
6. Capture calls PortablePropsBuilder.Capture. To rebuild all props, first save
   an independent copy of the lighter prefab's FlameVisual child as
   Assets/CreatorCommunity/LighterFlame/LighterFlame.prefab. Retain the imported
   README, license and author audio files. BuildTestCaptureExport rebuilds,
   checks, renders and exports. RebuildRadioTestCaptureExport limits the rebuild
   to the radio. All these operations are confined to the marked fixture.

## Findings Corrected Before Publication

- SDK allow-list failures were corrected by using GameObject.name and
  Rigidbody.linearVelocity in the native graphs.
- The replacement radio handle was aligned with the existing horizontal grab
  collider, preserving its dimensions and physical-contact flag.
- Inward triangle winding on the triangular-section handle was reversed;
  a per-triangle outward-normal assertion prevents regression.
- An initial placement harness assumed an unused layer. The configured SDK names
  every layer; the harness now temporarily uses its user layer and restores it.
  This was a test assumption, not a prop failure.
