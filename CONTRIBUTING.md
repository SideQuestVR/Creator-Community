# Share With SideQuest Creator Community

Contributions can be small. A useful prefab, graph, illustrated recipe or Editor
helper is enough. Keep the submission focused on one reusable result.

## Propose A Contribution

Use the [Share a creation form](https://github.com/SideQuestVR/Creator-Community/issues/new?template=contribution.yml).
You need a GitHub account, but not Git, the command line or your own repository.
The form creates a public submission issue. A maintainer handles review and can
prepare the pull request; a bot does not automatically publish or execute files.

Put `.unitypackage` or source files inside a ZIP for upload. Click the form's
file or screenshot box, then drag in your attachment or use its attachment
button. GitHub's attachment limits are 25 MB per ZIP and 10 MB per image. For a
larger package, provide a versioned download link instead. A recipe can be
text-only. See
[GitHub's attachment documentation](https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/attaching-files).

If you are unsure about versions or licensing, choose the help option or say
not sure. This does not grant redistribution rights: licence and permission
must be resolved before a package enters the directory. Do not upload files
while permission is pending. Attachments are public, not a private review inbox.

For the form or a direct pull request, include what you know:

- What it does, its author and the original source/discussion link where available.
- The files, or a public link to the exact version to review.
- Permission to redistribute and a licence supplied by the copyright holder.
- Exact Unity and Creator SDK or Banter versions actually tested.
- Dependencies, setup instructions, limitations and test results.
- Whether it includes C#, Editor scripts, executables or network access.
- Screenshots you have permission to share, with private details removed.

Do not upload credentials, private project paths, logs containing personal data,
paid assets without redistribution rights, or an entire unrelated Unity project.
Do not claim someone else's Discord post or package as your contribution.
AI-assisted work follows the same ownership, review and testing requirements.

## Pull Request Layout

Use a stable lowercase ID and a new directory for each package version:

```text
packages/
  author.package-name/
    1.0.0/
      listing.json
      README.md
      LICENSE.txt
      preview.png
      Package.unitypackage
```

The preview and package files are optional: a recipe can contain instructions
only, and a download may instead point to an exact GitHub Release asset or
approved external URL. Documentation must say what the user will receive.

Copy `examples/listing.json` and replace every example value. It is a structural
example only; its package file does not exist and it is not an approved listing.
The JSON schema is `schemas/listing.schema.json`. All relative asset paths are
repository-root-relative, not relative to the listing. Use HTTPS for external
assets, never local file URLs, private-network URLs or links containing secrets.

Record the exact download byte length and SHA-256. On Windows, SHA-256 can be
obtained with `Get-FileHash -Algorithm SHA256 <file>`; maintainers can help with
this step. A checksum identifies bytes, not whether their contents are safe.

Supply the package's actual licence text in its version directory. The listing's
licence identifier must agree with it; use an SPDX identifier when available.
Use `LicenseRef-...` only with the corresponding full custom licence text.
Do not select MIT or another licence on behalf of someone else's work.

## Storage And Releases

Small package files can be included in the PR. Larger binary packages should be
attached to a versioned GitHub Release by an authorized maintainer, or hosted in
the author's own release. For the shared repository, use a tag such as
`author.package-name-v1.0.0`. Avoid moving tags and mutable latest-download URLs.

Review the submission before publishing its release assets. A draft release is
not an approved directory entry. Never upload confidential material to a public
PR or rely on a draft as a private contributor inbox.

Once reviewed, a maintainer adds the listing path to `index.json`, for example:

```json
{
  "schemaVersion": 1,
  "entries": ["packages/author.package-name/1.0.0/listing.json"]
}
```

The initial index is deliberately empty. Do not add the example listing.
Hub integration is future work; these files do not install anything by themselves.

## Maintainer Review

Before indexing a version, check:

- Author permission, attribution and licence for every included asset.
- Exact file contents and hashes; no unexpected binaries or unrelated files.
- Declared Editor/runtime scope, script behavior and network or filesystem effects.
- Dependencies and compatibility claims against the supplied test evidence.
- Clear setup/removal instructions and possible conflicts with existing content.
- Distinction between compile checks and actual Editor, headset or multiplayer tests.

Review submitted code as data first. Do not execute it while generating the index
or in privileged CI. Any test execution needs a separately approved isolated
environment. An approved version does not approve future upstream updates.

After publication, submit corrections through an issue or PR. Preserve published
version history; remove a problematic version from discovery with an explanatory
notice rather than silently changing the package bytes. Sensitive security reports
should not include exploitation details or private data in a public issue.
