# GitHub releases / 独立发布流程

GitHub and Steam Workshop are independent release channels. This repository does
not automatically upload to Steam or publish a release when a commit/tag is pushed.
Publish only after the maintainer explicitly chooses the candidate and version.

Current development: **Librarian 1.3.0 unified**, with one loader, two content DLLs
and one shared PCK targeting **0.107.1 / 59260271** and **0.111.0 / 41cef1ea**.
Its compile baseline is RitsuLib 0.6.4 only. Future formal source maintenance uses
`main`; historical `codex/beta`, stable snapshots, tags and release assets remain
intact. The last published beta is `v1.2.0-beta12`; this page does not declare
V1.3.0 published or its native validation complete.

## Unified V1.3.0 preparation

Prepare only after both native targets have passed the release's own menu/new-run/
combat/save/reload and complete runtime-log gates. The versioned Delivery must
identify `channel: unified`, both exact game version/commit/branch targets,
`validation.passed`, separate `validation.targets[version].passed` values,
RitsuLib 0.6.4, all six artifacts, and matching complete `source_before` / `source_after`
provenance snapshots. Each snapshot's SHA256 must match the builder's canonical
JSON file-summary digest. Source export also checks the selected source files;
release preparation checks the recorded digest without treating a public copy
that omits private configuration as an identical research workspace.

```powershell
python scripts/export_github_source.py --channel unified --source-workspace . --delivery docs/Revision-v1.3.0-Delivery.json
python scripts/prepare_github_release.py --channel unified --candidate outputs/revision-v1.3.0/candidate/Librarian --version 1.3.0 --delivery docs/Revision-v1.3.0-Delivery.json --output outputs/revision-v1.3.0/github-assets-r1
```

The exporter writes only the allowlisted snapshot to ignored `.research/github-unified`;
an existing Git checkout must be on `main`. It never copies the research history,
private SDKs or dependencies. The release preparer requires a new output directory
and preserves candidate bytes. Both tools prepare locally, without tags, uploads
or publication. Internal native runners/log-policy tools are not exported.

GitHub has a flat attachment namespace. Root `Librarian.dll` is the loader;
`compat-0.107.1-Librarian.dll` and `compat-0.111.0-Librarian.dll` map to
`mods/Librarian/compat/<version>/Librarian.dll` through `INSTALL-MAP.json`.
Attach all six runtime assets, installation map/instructions, notices and
`SHA256SUMS.txt` to a reviewed draft for the exact public-source tag `v1.3.0`.
The unified release is a formal release, not a beta prerelease. Do not create the
tag or publish until explicitly authorized, and do not rewrite historical tags.
Download and verify every published attachment before claiming delivery.
No full ZIP is produced by default; dependencies are installed separately.

Steam publication remains independent. Publish the new unified package to
3801958367 before releasing the old 3811677053 final migration notice. The old
package retains its original double dependencies and exact V1.1.1 content with
the new notice only. Steam branch delivery and subscriber file hashes require
their own verification.

## Historical three-file releases

1. Finish the release's source changes and compatibility/native validation. Record
   exact game/build, BaseLib/RitsuLib versions, known limitations and source commit.
2. Prepare a reviewed candidate containing exactly `Librarian.dll`, `Librarian.json`
   and `Librarian.pck` from one validated build. Dependency/runtime files stay separate.
3. Run the local release preparation tool. It checks the manifest and creates a new
   output directory containing those three files, licenses/notices, and checksums.
   In the private maintainer workspace also provide the matching Delivery JSON.

```powershell
python scripts/prepare_github_release.py --candidate PATH_TO_CANDIDATE --version 1.0-beta3 --output .research/github-release/1.0-beta3 --delivery docs/Revision-v1.0-beta3-Delivery.json
```

Outside the private workspace omit `--delivery` and separately verify the candidate
against the approved build record. Without that record the script only validates
package structure, versions and file hashes, not prior runtime approval.

4. Push the reviewed public source commit. Create an annotated `v<version>` tag
   at that exact commit and push that explicit tag (never `--mirror` or `--all`
   from the research workspace).
5. Create a **draft** GitHub release for the existing tag, attach the three runtime
   files, `LICENSE`, `ASSET-LICENSE.md`, `THIRD-PARTY-NOTICES.md`, the individual
   license texts/inventory and `SHA256SUMS.txt`; add installation and compatibility
   notes. Beta versions must be marked **pre-release**.
6. Review draft assets and checksums, then publish deliberately. Download the live
   assets and compare hashes before recording GitHub delivery as verified.

Example with authenticated GitHub CLI, after the tag exists:

```powershell
gh release create v1.0-beta3 --repo albertoBradish/Librarian-StS2 --verify-tag --draft --prerelease --title 'Librarian 1.0-beta3' --notes-file RELEASE_NOTES.md
```

The example creates an empty draft; upload **every file** from the prepared output
directory via `gh release upload` before review. Do not use an unreviewed wildcard
over the workspace, bundle BaseLib/RitsuLib/game DLLs, publish PDBs, or distribute
research data. A ZIP is optional and is not produced by default. Public source
archives are not substitutes for the three mod runtime files.

Release assets use a flat namespace: the tool writes license attachments as
`licenses-<filename>` and adjusts Markdown notice links to match. Keep all those
attachments together when downloading. Every release must be free to access and
retain the custom license and upstream notices; no paywall or paid-content workflow
is authorized. See [LICENSE-AUDIT.md](LICENSE-AUDIT.md) for Mega Crit's policy.

GitHub Issues accepts feedback independently of Workshop visibility. A GitHub
release does not prove that Steam subscribers received the same build.

## Historical archives / 历史二进制归档

Small beta revisions retain the base version and increment `betaX` by default: `1.1.0-beta1` → `1.1.0-beta2`. A larger version change or an explicit author instruction may select a new base version. Git source synchronization and a new GitHub Release are separate actions.

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
