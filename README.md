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

**Development status:** this branch prepares the catalogue for the shared
Creator Plugins page in Creator Hub, Creator Project Setup and Creator Works
MCP. It is not a separate app. Start Location by Mr. E / egon.gb is the first
maintainer-approved listing. See its licence and testing notes before importing.
App features depend on the version installed; catalogue publication does not
install or update an app.

## Browse

The categories are:

- **Prefabs and graphs:** reusable scene objects and Visual Scripting examples.
- **Recipes:** instructions, screenshots and small working examples.
- **Plugins:** SDK extensions and larger integrations.
- **Editor tools:** Unity Editor scripts and utilities, including AI-assisted creations.
- **MCP tools:** reusable MCP tools, servers and integrations for AI clients.
- **AI skills:** reusable instructions and workflows for AI assistants.

MCP tools and AI skills have their own filters. Listings can link to instructions
or offer reviewed ZIP downloads; browsing never installs a server, enables a
skill or changes AI-client settings. Only `.unitypackage` files can be sent to
Unity for import review.

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

The [index](index.json) points to version-specific listing files. Pending entries
can be previewed, but only `reviewStatus: "listed"` enables an app download.
Each download records its size and SHA-256 checksum. New versions get new
paths or release tags; do not silently replace an existing published package.

The starter listing format is documented in [the schema](schemas/listing.schema.json).
It is a community-directory format, not a Unity Package Manager registry or the
signed first-party Creator Hub app catalogue. Merely browsing a listing never
authorizes importing or running it.

The apps read the fixed `main/index.json` feed from this repository. To add a
listing, create `packages/<author.package>/<version>/listing.json`, add its path
to the index, and submit the change for review. Use `previewImage` for an optional
repository image or SideQuest CDN URL, `author.discord` for credit, and `usage`
and `contents` for instructions and bundled files. Exact tested versions belong
in `compatibility`; leave arrays empty when unverified. Do not mark a download
listed before its licence and contents have been reviewed.

The development apps download packages up to 32 MiB, check exact size and SHA-256,
and offer saving or an explicit project selection. Their optional Editor-only
menu queues packages outside `Assets` for the user's review in Unity's package
import dialog. Browsing or queueing never silently imports code. These new
actions are local development work, not yet shipped. There are no accounts or
separate community service.

## Review And Testing

Maintainers review provenance, licence, contents, dependencies and compatibility
before adding a contribution to the index. A submission is not automatically
published, built or executed. Project modification requires separate user
approval and Unity import review; an import receipt is not proof of package
compatibility or safe behavior.

Try community packages in a backed-up test project first. Report the package
version, Unity version, SDK version and what actually happened. Compilation
alone does not prove in-headset or multiplayer behavior.

There are no fees or commissions. This repository's original directory
documentation and tooling use the [MIT licence](LICENSE). **Contributed packages
retain their own explicitly supplied licences.** Never assume the repository's
licence grants rights to a third-party package or linked download. The official
[SideQuest brand assets](assets/README.md) are excluded from the MIT licence.
