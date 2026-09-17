# Supplemental Downloads

Large downloads use root `downloads.json`. Do not put downloads over 32 MiB in
`listing.json`: released older clients reject the entire listing in that case.
Do not add new fields to a listing to work around that limit.

Keep a normal reviewed listing, with `download` absent or null, plus its usage,
licence, dependencies and runtime/editor scope. Add an exact `id` and `version`
match to `downloads.json` only after compatible consumer release acceptance.
An absent or unavailable sidecar leaves the listing visible to all clients.
New clients never use this file to replace an existing listing checksum.

Example format (placeholder data, not an active submission):

```json
{
  "schemaVersion": 1,
  "downloads": [{
    "id": "example.creation",
    "version": "1.0.0",
    "download": {
      "url": "https://cdn.sidequestvr.com/file/123/example.unitypackage",
      "byteLength": 93245650,
      "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  }]
}
```

- Maximum package: 256 MiB; sidecar: 128 KiB, 50 unique IDs.
- Size and SHA-256 must describe the exact raw download, not its ZIP wrapper.
- Only `.unitypackage` is eligible for Unity import. ZIP stays download-only.
- The desktop reviewer streams archive inspection with a 2 GiB expanded bound
  and 20,000 member limit. Links, unsafe paths and conflicting GUIDs are refused.
- These are integrity and structural checks, not malware or compatibility proof.
- Project selection, matching Unity helper and Unity file-selection consent remain.

Consumer support is being tested for Hub 0.1.7, Setup 0.3.1 and MCP 2.7.2 with
Unity helper 0.1.1. **Do not activate downloads based on this document alone.**
Wait for the app release task to confirm published compatible artifacts and import
acceptance. This initial sidecar is intentionally empty.
