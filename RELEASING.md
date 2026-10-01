# Stable release workflow

This branch builds **Librarian 1.1.0 for game 0.107.1 stable** only. Both **BaseLib 3.4.5** and the complete **RitsuLib 0.6.2** bundle matching game API 0.107.1 are required. Use the separate stable Workshop item 3809003723. The beta counterpart is **1.1.0-beta1**, tag **`v1.1.0-beta1`**, game 0.111.0 on `codex/beta` / item 3801958367.

The formal tag is **`v1.1.0-stable`**. The mod manifest version remains **`1.1.0`**. Historical tags, including `v1.0.0` and `v1.0.0-stable`, must not be moved or overwritten.

Prepare the reviewed three-file candidate with `python scripts/prepare_github_release.py --channel stable --candidate PATH --version 1.1.0 --output NEW_DIRECTORY --delivery VERIFIED_DELIVERY.json`. The Delivery record must include `channel: stable`, `validation.passed: true`, `version` and exact `build.artifacts` hashes. Never mark those fields without completed native validation.

Create an annotated tag on its matching public source commit, then a draft Release, attach all runtime files and notices, review, publish as a normal release, and download every asset to verify SHA256. Steam upload and GitHub publication require deliberate authorization. Do not overwrite published files or mix channels; publish a new version for changed bytes. Keep the free access, original-content license and upstream notices.

## Historical archives / 历史二进制归档

The author authorized migration of verifiable local historical versions on
2026-09-27. For older binaries without a complete matching source snapshot, an
annotated version tag points to an independent **metadata-only** commit containing
provenance, checksums and license notices. This is an explicit archival exception
to the source-commit rule above; it must never be presented as the old source tree.
Automatic Source code archives for those tags contain metadata, not game source.
The historical 1.0-beta3 tag retains its matching source snapshot.

Archive assets must match either the historical Delivery hashes or the verified
original package/upload snapshot. ZIPs containing dependencies are not reuploaded;
only the three mod files are extracted. BaseLib-only versions must use the explicit
`--historical` preparation flag with verified artifact evidence. Preserve old bytes,
dependency versions and warnings; do not rebuild or edit manifests to invent an
old release. Unavailable versions are listed in `release-history.json`.

All migrated test versions are prereleases. In particular, historical `1.0.0`
is a local bilingual candidate preceding beta2/beta3, not a newly declared stable
version. A rejected historical log gate must remain visible in provenance/notes;
archive integrity is not a new native runtime approval.

Use Git for source/provenance and Releases for runtime history. Restore files with:

```powershell
gh release download v1.0-beta3 --repo albertoBradish/Librarian-StS2 --dir PATH_TO_NEW_DIRECTORY
```

Then verify every entry in `SHA256SUMS.txt`, particularly all three runtime files,
before using the restored version. Do not replace published assets/tags in place;
publish a distinct version for changed bytes. Local caches are optional after
download verification, but original designs, private dependencies and referenced
validation evidence are not disposable release caches.
