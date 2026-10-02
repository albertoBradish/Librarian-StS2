# Stable release workflow

This branch builds **Librarian 1.1.1 for game 0.107.1 stable** only. Both **BaseLib 3.4.5** and the complete **RitsuLib 0.6.2** bundle matching game API 0.107.1 are required. Use the separate stable Workshop item 3811677053 on the `public` game branch. The current beta source and Steam counterpart is **1.1.0-beta6**, game 0.111.0 on `codex/beta` / item 3801958367; its latest separate GitHub runtime release remains **`v1.1.0-beta1`**.

The current mod manifest version is **`1.1.1`**. This update publishes stable Steam content and synchronizes the allowlisted `main` source; it does not create a separate GitHub Release. The latest published GitHub stable runtime release remains **`v1.1.0-stable`**, with its original verified attachments. Historical tags, including `v1.0.0`, `v1.0.0-stable`, and `v1.1.0-stable`, must not be moved or overwritten.

For a separately authorized future GitHub runtime release, prepare the reviewed three-file candidate with `python scripts/prepare_github_release.py --channel stable --candidate PATH --version 1.1.1 --output NEW_DIRECTORY --delivery VERIFIED_DELIVERY.json`. The Delivery record must include `channel: stable`, `validation.passed: true`, `version` and exact `build.artifacts` hashes. Never mark those fields without completed native validation.

For source synchronization, run `python scripts/export_github_source.py --channel stable` after the new stable build and Delivery are verified. The exporter reads the isolated stable source and the `github/stable` document overrides into `.research/github-stable`; review the allowlist, source byte equality, restrictive license, and pure rule tests before pushing only `HEAD:main`. Verify the remote commit, full blob set, repository visibility, Issues, and the CI run for that commit. Preserve `codex/beta` and the root beta source.

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
