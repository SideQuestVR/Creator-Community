# Start Location

By **Mr. E**, Discord **egon.gb**. Description and usage are paraphrased from
the author's Start Location Discord post provided by the maintainer.
The original Discord permalink has not yet been supplied.

Assign the Start-Location graph to a Script Machine on the GameObject you want
to use as the starting location. On Start calls Set Space Settings, with This
connected to Spawn Object. Review all the other space settings before use;
the graph changes more than the starting position.

## Review Status

Approved for public listing by the maintainer on 2026-09-15. The reported licence
is "Open licence"; see [licence notes](LICENSE-NOTES.md). No named licence was
supplied, and MIT must not be assumed. Listing approval is not installation
approval or a compatibility guarantee. The catalogue version `0.0.0-review.1`
identifies this catalogue entry, not an author release.

The 16,046-byte archive was inspected without extracting into a Unity project.
It contains these original paths (including spaces):

- `Assets/Start-Location.asset`
- `Assets/teleport-OnStart.unity`
- `Assets/Samples/SideQuest Creator SDK/4.0.14/Basics/-SharedAssets-/Subgraphs/SpaceSettings/SpaceSettings.asset`
- `Assets/TutorialInfo/Scripts/Editor/ReadmeEditor.cs`
- `Assets/TutorialInfo/Scripts/Readme.cs`

The readme editor has InitializeOnLoad behavior and tutorial-removal controls.
Importing C# can execute code. Check existing assets and GUID collisions before
any import; a checksum proves file identity, not safety. No Unity version, SDK
compatibility, headset or multiplayer test has been completed for this package.

Review the files and test the graph in a backed-up project before using it.
If the author supplies a smaller package, publish it under a new version and hash.
