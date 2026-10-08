# Creator Plugins Retirement

The Creator Plugins integration in the Creator apps is ending on **20 October
2026 at 00:00 UTC**. This refers to the in-app catalogue, not to deleting
community creations or ending the rest of Creator Hub.

## What Changes

- Updated Creator Hub, Creator Works MCP and Project Setup show a retirement
  notice instead of the catalogue. New catalogue fetches, package downloads,
  import requests and installation of the optional Unity catalogue menu are
  disabled in that update.
- Before the cutoff, the notice offers
  [creatorplugins.store](https://creatorplugins.store/).
- At and after the cutoff, the updated app shows the retired notice without
  that link. The date check is built into the app, works offline and also applies
  to a notice that was already open. It uses the computer's current clock.

## What Stays

Existing imported assets, Unity project files, downloaded packages and import
receipts are not removed by this retirement. Each package retains its own licence.
MCP's scene, prefab, component and Visual Scripting operations are not retired.
The repository remains a historical record; published package bytes and links
are not being rewritten as part of this documentation change.

The store is separate from the retired in-app catalogue. This notice does not
promise its availability, package compatibility or support.

## Older Versions

Install the Creator app update containing the retirement notice to get this
behaviour. The change cannot remotely rewrite older app binaries or an existing
Unity catalogue window that has not been updated. Historical import and
catalogue instructions in this repository describe the older integration and
are not a promise of continued app support.

## Problems With Existing Assets

Keep a backup before changing a Unity project. Report the exact package, Unity
and SDK versions, the action attempted, the actual result and relevant errors
to its maintainer. Remove credentials and private project information before
sharing logs. Existing licences and maintainer support terms still apply.
