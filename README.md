# Creator Community

A community collection of recipes, prefabs, Visual Scripting graphs, plugins
and reusable tools for SideQuest creators.

Small, useful contributions belong here too: a spawn-point prefab, a working
interaction graph, a short guide, or an Editor utility. A contribution does not
need to be a full plugin or have its own repository.

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

**No Git experience?** [Open a contribution proposal](https://github.com/SideQuestVR/Creator-Community/issues/new?template=contribution.md).
Describe what you made and provide a link to the files, repository or original
discussion. A maintainer can help prepare the listing. If you cannot share a
public download, say so in the proposal and arrange a reviewed handoff first.

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
licence grants rights to a third-party package or linked download.
