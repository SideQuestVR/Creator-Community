<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/sidequest-logo-white.png">
  <source media="(prefers-color-scheme: light)" srcset="assets/sidequest-logo-black.png">
  <img alt="SideQuest" src="assets/sidequest-logo-black.png" width="220">
</picture>

# SideQuest Creator Community

A community collection of recipes, prefabs, Visual Scripting graphs, plugins
and reusable tools for SideQuest creators.

Small, useful contributions belong here too: a spawn-point prefab, a working
interaction graph, a short guide, or an Editor utility. A contribution does not
need to be a full plugin or have its own repository.

## [Share a creation](https://github.com/SideQuestVR/Creator-Community/issues/new?template=contribution.yml)

Tell us what it does, add a ZIP or link, and include a screenshot if you have one.
No Git, terminal or pull request experience needed. Sign in to GitHub to submit;
a maintainer will help with the listing and PR. Not sure about versions or a
licence? Say so in the form. Submissions and uploaded files are public.

**Status:** the directory and contribution process are being established.
There are no published packages yet. Creator Hub browsing and installation are
not connected to this repository yet.

## Browse

The planned categories are:

- **Prefabs and graphs:** reusable scene objects and Visual Scripting examples.
- **Recipes:** instructions, screenshots and small working examples.
- **Plugins:** SDK extensions and larger integrations.
- **Community tools:** Editor scripts, utilities and optional MCP-assisted tools.

Listings identify their author, licence, dependencies and tested Unity/SDK
versions. Banter and Creator SDK compatibility are recorded separately.
Hosting here is not a guarantee of safety, compatibility or SideQuest endorsement.
AI assistance is not a quality or safety certification.

## Contribute

**No Git experience?** Use the [guided submission form](https://github.com/SideQuestVR/Creator-Community/issues/new?template=contribution.yml).
Attach a ZIP containing your package, or link to the exact version or original
discussion. Instructions-only contributions are welcome. This creates a public
proposal for review, not a PR or an automatically published package.

**Comfortable with GitHub?** Submit a pull request with a versioned contribution
folder and its listing. See [CONTRIBUTING.md](CONTRIBUTING.md) and the
[example listing](examples/listing.json).

Only submit work you own or have permission to redistribute, including any
screenshots, models and other bundled assets. A file being posted publicly on
Discord does not establish a licence or permission to copy it here.

## Where Files Live

The listing, instructions, screenshots and small packages can live in this
repository. Larger or frequently updated downloads should use a versioned
GitHub Release asset. An author's own repository or an approved CDN URL is also
supported; a separate CDN is not required.

The [index](index.json) will point to approved, version-specific listing files.
Each download records its size and SHA-256 checksum. New versions get new
paths or release tags; do not silently replace an existing published package.

The starter listing format is documented in [the schema](schemas/listing.schema.json).
It is a community-directory format, not a Unity Package Manager registry or the
signed first-party Creator Hub app catalogue. Merely browsing a listing never
authorizes importing or running it.

## Review And Testing

Maintainers review provenance, licence, contents, dependencies and compatibility
before adding a contribution to the index. A submission is not automatically
published, built or executed. Installation and project modification require a
separate, future reviewed workflow.

Try community packages in a backed-up test project first. Report the package
version, Unity version, SDK version and what actually happened. Compilation
alone does not prove in-headset or multiplayer behavior.

There are no fees or commissions. This repository's original directory
documentation and tooling use the [MIT licence](LICENSE). **Contributed packages
retain their own explicitly supplied licences.** Never assume the repository's
licence grants rights to a third-party package or linked download. The official
[SideQuest brand assets](assets/README.md) are excluded from the MIT licence.
