# Lighting Tools Catalogue Review

Reviewed on 2026-09-15 for Creator Community PR #1 by Ladypoly (Elin),
head `4bc43b9407e4e12a331d90bc05d7c8b0ff27447d`.

## Package Checks

- All five original 1.0.0 archives match their published byte lengths and SHA-256 values.
- Archive asset paths stay inside `Assets/SideQuest/LightingTools`, including its parent folders.
- C# and assembly-definition payloads match the submitted frozen source snapshots,
  allowing only CRLF/LF differences introduced by the Git checkout.
- The 40 common asset/folder records have identical paths, GUIDs, metadata and content
  across the packages. No conflicting shared assets or GUID collisions were found.
- MIT licence files and Elin's attribution are retained. Package bytes were not rebuilt.
- Listing JSON is UTF-8 without a byte-order mark. The catalogue and listings validate
  against the repository's JSON Schema 2020-12 definitions.

## Independent Unity Check

A disposable Windows project used Unity **6000.3.21f1**, **URP 17.3.0** and a classic
light-probe pipeline. It contained no Creator SDK, Banter SDK or user world.

The actual five `.unitypackage` archives were imported through
`AssetDatabase.ImportPackage`, sequentially, waiting for each
`AssetDatabase.importPackageCompleted` event. The Editor was then reopened for
compilation and tests against those imported files, with the contributor's
`Tests/Editor` scripts added separately.

The generated T2 four-room fixture passed:

- Light Probes: Analyze, Accept Recommendations, Preview and Apply Plan (No Prompt).
  All **1,341 probes** stayed inside the room bounds despite the group's translated
  and rotated transform. Probes spanned **2.55 m / seven height buckets**.
- No movable renderer contributed GI.
- Reflection Probes: Analyze, Accept, Preview and Apply created a reflection probe.
- Occlusion: Analyze, Accept, Preview and Apply. All **24 walls** occluded; glass and
  alpha-clipped foliage were occludee-only; the moving crate was neither. The final
  classification contained **32 occluders and 37 occludees**.
- Light Bake: Analyze, Accept and Apply produced a LightingSettings asset.
- All **five contributor regression checks** passed. The batch exited with code 0
  and no captured error, exception or assertion during the smoke sequence.

The first harness attempted to quit immediately after requesting imports. Those
queued requests were not evidence of completed imports. It was corrected to wait
for completion events before the results above were recorded.

## Limits

This independent pass did **not** repeat lightmap/reflection/occlusion baking,
the stress fixture, interactive cancellation/Undo, other Unity versions, macOS,
Linux, headset use, multiplayer, Creator SDK/Banter integration or AssetBundle
deployment. The contributor's broader tests remain separately attributed in the
listing notes. Listing approval is not a guarantee of security or compatibility.

## Catalogue Presentation

All five packages are Editor tools. Core is already embedded in each feature
package; keep installed SideQuest lighting tools at the same version. Installation
instructions and limitations stay in each package's README.

The shared preview is original category artwork, not a plugin screenshot. Source
links target the reviewed commit rather than nonexistent release tags. The desktop
and Unity catalogues both read the shared `main/index.json`; no application release
is required for these entries to become available after a catalogue refresh.
