# Preview Galleries

Keep `listing.json.previewImage` as a PNG/JPEG cover. Optional galleries live in
the root `media.json`, matched by listing `id`. Do not add media fields to
`listing.json`: released older consumers reject unknown fields.

- Schema: [media.schema.json](schemas/media.schema.json).
- [Six-image example](examples/six-image-gallery.json) and
  [animated example](examples/animated-gallery.json) use placeholder paths, not
  public catalogue submissions.
- Add or replace only your listing's gallery; preserve other galleries.
- Up to 8 ordered items per gallery, 50 unique gallery IDs, 256 KiB JSON total.
- `type: image`: PNG/JPEG `url`, no `poster`.
- `type: gif` or `webm`: matching file extension and a required PNG/JPEG `poster`.
- Locations must be repository-relative or on the approved SideQuest CDN or
  this repository's raw `main` host. No credentials, query strings or redirects.
- Static images, posters and GIFs: at most 2 MiB each. Static images: at most
  4096 pixels per axis and 4 megapixels. WebM: at most 16 MiB; use short clips.
- The cover comes first unless already present in the gallery. Include the
  exact cover URL as the first item to show precisely six images, not seven.
- Desktop galleries load animation/video only on request. WebM playback depends
  on the installed browser/WebView codec support; the poster remains available.
- Unity displays PNG/JPEG images and static posters only, not GIF/WebM playback.
- Older apps continue displaying the single cover. Users need an updated app
  and, for Unity, an explicitly updated Creator Plugins menu package.

Unavailable or invalid galleries fall back to the cover without hiding the
listing or changing package import permissions. Media is presentation data,
not code-safety verification or installation authority. This does not change
package-download size limits.
