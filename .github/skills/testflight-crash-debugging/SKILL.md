---
name: testflight-crash-debugging
description: "Workflow for debugging Masjid App iOS/TestFlight crashes using crash-debugging artifacts retained by the publish workflow. USE WHEN analyzing Apple .crash or .ips reports, SIGABRT, EXC_CRASH, unsymbolicated MasjidApp frames, dSYM UUID mismatches, atos/symbolicatecrash output, xcarchive recovery, GitHub Actions artifacts, build-number correlation, or managed .NET MAUI/AOT crashes. Trigger phrases include TestFlight crash, iOS crash report, Binary Images UUID, dSYM, DWARF, xcarchive, symbolicate, atos, build artifact, ios-crash-debug, Mono abort, and missing symbols."
argument-hint: "Path to an Apple crash report, or an iOS version/build and incident details"
---

# TestFlight Crash Debugging

Use this workflow to turn an Apple crash report into the strongest conclusion supported by the exact artifacts uploaded during the corresponding TestFlight publish run.

The publish workflow is [publish-appstore.yml](../../workflows/publish-appstore.yml). Its artifact layout and retention policy are documented in [PUBLISHING.md](../../../docs/PUBLISHING.md).

## Hard Rules

- Use only the dSYM whose UUID matches the crashed `MasjidApp` image. A rebuild from the same commit is not equivalent and must not be used for final symbolication.
- Verify `CHECKSUMS.sha256` before trusting extracted files.
- Keep downloaded archives and extracted signed bundles under `$TMPDIR`, not in the workspace.
- Never upload the retained archive to a public release or third-party service. It contains the signed app, provisioning profiles, native symbols, and managed symbols.
- Separate confirmed facts, likely conclusions, and unknowns. A Mono abort stack alone does not reveal the managed exception type.
- Do not edit code until the exact symbols identify an app-owned path, or the available evidence supports one falsifiable local hypothesis and one discriminating check.
- Preserve caller-requested cancellation when hardening async code. Do not turn cancellation into a generic failure result.

## Artifact Contract

A successful archive run uploads an artifact named:

```text
ios-crash-debug-<version>-<build>-<commit-prefix>-run<attempt>
```

The artifact contains:

- `MasjidApp-*.xcarchive.zip`: signed app, archive metadata, and all dSYMs.
- `MasjidApp-*.dSYMs.zip`: direct dSYM bundle.
- `MasjidApp.ipa`: exact package passed to `altool`, when upload credentials were available.
- `MasjidApp-*.managed-symbols.zip`: compiled and linked app-owned DLL/PDB pairs.
- `MasjidApp.binlog`: MSBuild binary log.
- `publish-appstore.log`: publish, validation, and upload log. Runs from before the workflow rename contain `publish-testflight.log` instead.
- `metadata/build-manifest.txt`: source commit/tree, version, build, workflow run, and app UUID.
- `metadata/app-uuids.txt` and `metadata/dsym-uuids.txt`.
- SDK, workload, signing, entitlement, dependency, git-status, and version-bump metadata.
- `CHECKSUMS.sha256`.

Runs that fail before archive creation may instead upload `ios-publish-diagnostics-*`, which cannot symbolicate a runtime crash.

## Procedure

### 1. Extract Crash Identity

Read the report before searching code. Record:

- incident identifier;
- `Version`, including marketing version and build number;
- crash and launch timestamps;
- OS version and hardware model;
- exception type, termination reason, and triggered thread;
- the `MasjidApp` line under `Binary Images`;
- the image load address and image UUID;
- all absolute `MasjidApp` addresses on the crashed thread.

Example `Binary Images` shape:

```text
0x104d74000 - 0x10854bfff MasjidApp arm64 <dd90472682d73789b4cb930dd8de6846> ...
```

Normalize UUIDs before comparing because Apple crash reports often omit hyphens and use lowercase:

```zsh
normalize_uuid() {
  printf '%s' "$1" | tr -d '<>-' | tr '[:lower:]' '[:upper:]'
}
```

Do not infer the cause from `SIGABRT` alone. On .NET MAUI iOS it commonly represents a fatal unhandled managed exception, but it may also represent a native assertion or explicit abort.

### 2. Locate The Publish Artifact

Prefer the repository helper, which performs artifact selection, checksum verification,
bundle extraction, and crash/app/dSYM UUID matching in one operation:

```zsh
.github/skills/testflight-crash-debugging/scripts/fetch-crash-artifacts.sh \
  docs/crashes/<incident>.crash
```

The helper prints a `case-summary.txt` path and the verified DWARF path for `atos`. Useful
options include:

```zsh
# Restrict an ambiguous lookup to one workflow run or artifact.
.github/skills/testflight-crash-debugging/scripts/fetch-crash-artifacts.sh \
  <report> --run-id <id> --artifact-name <name>

# Verify an artifact that was downloaded or restored manually.
.github/skills/testflight-crash-debugging/scripts/fetch-crash-artifacts.sh \
  <report> --artifact-dir <directory>

# Supply identity fields for an .ips shape the text parser cannot recognize.
.github/skills/testflight-crash-debugging/scripts/fetch-crash-artifacts.sh \
  <report.ips> --version <version> --build <build> --uuid <uuid>
```

See [fetch-crash-artifacts.sh](./scripts/fetch-crash-artifacts.sh). Use the manual steps
below when investigating artifact discovery itself or when GitHub CLI access is unavailable.

Confirm GitHub CLI access:

```zsh
gh auth status
```

List likely workflow runs around the crash build/date:

```zsh
gh run list \
  --workflow publish-appstore.yml \
  --limit 100 \
  --json databaseId,headSha,createdAt,conclusion,url,displayTitle
```

Inspect a candidate run's artifacts:

```zsh
RUN_ID=<workflow-run-id>
gh api "repos/bothellmosque/masjidapp/actions/runs/$RUN_ID/artifacts" \
  --jq '.artifacts[] | [.name, .expired, .size_in_bytes] | @tsv'
```

Choose the artifact whose version/build match the crash. When several attempts exist, prefer the attempt whose manifest and UUID match; do not assume run 1 is correct.

Download into a fresh case directory:

```zsh
CASE_DIR=$(mktemp -d "$TMPDIR/masjid-crash.XXXXXX")
ARTIFACT_NAME=<exact-artifact-name>
mkdir -p "$CASE_DIR/artifact"
gh run download "$RUN_ID" \
  --name "$ARTIFACT_NAME" \
  --dir "$CASE_DIR/artifact"
```

If the artifact has expired or is missing, check private durable storage and App Store Connect for the original dSYM. If no exact UUID match can be recovered, state that exact symbolication is blocked. Do not substitute a local rebuild.

### 3. Verify Integrity And Provenance

Run checksums from the artifact root because manifest paths are relative:

```zsh
(
  cd "$CASE_DIR/artifact"
  shasum -a 256 -c CHECKSUMS.sha256
)
```

Stop and redownload if any checksum fails.

Read provenance:

```zsh
cat "$CASE_DIR/artifact/metadata/build-manifest.txt"
cat "$CASE_DIR/artifact/metadata/app-uuids.txt"
cat "$CASE_DIR/artifact/metadata/git-status.txt"
cat "$CASE_DIR/artifact/metadata/version-bump.diff"
```

Verify all of the following:

1. Manifest version/build equals the crash report version/build.
2. Artifact commit is the publish source revision.
3. Normalized crash UUID equals a UUID in `metadata/app-uuids.txt`.
4. The corresponding UUID appears in `metadata/dsym-uuids.txt`.

A version/build match without a UUID match is insufficient. Search another workflow run or attempt.

### 4. Extract The Exact Symbols

Use `ditto` to preserve macOS bundle structure:

```zsh
mkdir -p "$CASE_DIR/unpacked/archive" "$CASE_DIR/unpacked/dsyms"
ditto -x -k "$CASE_DIR/artifact"/*.xcarchive.zip "$CASE_DIR/unpacked/archive"
ditto -x -k "$CASE_DIR/artifact"/*.dSYMs.zip "$CASE_DIR/unpacked/dsyms"

ARCHIVE=$(find "$CASE_DIR/unpacked/archive" -type d -name '*.xcarchive' -print -quit)
APP="$ARCHIVE/Products/Applications/MasjidApp.app"
BINARY="$APP/MasjidApp"
DSYM=$(find "$CASE_DIR/unpacked/dsyms" -type d -name 'MasjidApp.app.dSYM' -print -quit)
DWARF="$DSYM/Contents/Resources/DWARF/MasjidApp"
```

Recheck the extracted binaries rather than trusting filenames:

```zsh
xcrun dwarfdump --uuid "$BINARY"
xcrun dwarfdump --uuid "$DWARF"
```

If the UUIDs differ from each other or from the crash report, stop. The artifact is not usable for that crash.

### 5. Symbolicate Native And AOT Frames

For focused frames, use the image load address from `Binary Images` and absolute addresses from the crashed thread:

```zsh
LOAD_ADDRESS=0x104d74000
atos -arch arm64 -o "$DWARF" -l "$LOAD_ADDRESS" \
  0x1082925fc 0x10820b148 0x1082708c4
```

Pass several contiguous `MasjidApp` frames, not only the top frame. Runtime abort frames often sit above the useful callback or AOT frame.

For full-report symbolication, first try Xcode Organizer with the matching `.xcarchive` and crash report. If command-line full symbolication is needed, locate Xcode's `symbolicatecrash` utility inside the selected Xcode bundle and run it with the matching dSYM available. Treat `atos` output as the simpler, more reproducible focused check.

Interpret results carefully:

- App-owned method/file: trace that exact code path at the artifact commit.
- Plugin or UIKit method: inspect the app callback that invoked it and the API's thread/permission contract.
- Mono runtime abort/exception machinery only: confirms runtime termination behavior but usually not the managed exception type.
- `atos` returns raw addresses or wrong symbols: recheck architecture, absolute versus offset addresses, load address, and UUID before investigating code.

The dSYM's `Contents/Resources/Relocations/aarch64/MasjidApp.yml` is part of MAUI's AOT symbol information. Preserve it with the dSYM; do not copy only the DWARF file.

### 6. Recover Managed Context

Apple crash reports often omit the managed exception message and managed stack. The managed-symbol archive cannot manufacture a missing runtime stack, but it preserves the exact app assemblies/PDBs for correlating managed frames from telemetry, device console logs, or TestFlight feedback.

Extract it when managed evidence exists:

```zsh
mkdir -p "$CASE_DIR/unpacked/managed"
ditto -x -k "$CASE_DIR/artifact"/*.managed-symbols.zip \
  "$CASE_DIR/unpacked/managed"
find "$CASE_DIR/unpacked/managed" -type f \( -name '*.dll' -o -name '*.pdb' \) -print
```

Use `compiled/` to understand the pre-link app build and `linked/` to match the trimmed/linker input retained during publishing. Use `project.assets.json`, `dotnet-info.txt`, `dotnet-workloads.txt`, and `xcode-version.txt` to resolve dependency or toolchain differences.

`publish-appstore.log` and `MasjidApp.binlog` describe the build and upload, not runtime behavior. Do not present them as runtime crash logs.

### 7. Correlate With Exact Source

Use the commit from `metadata/build-manifest.txt`, not current `HEAD`:

```zsh
COMMIT=<manifest-commit>
git show "$COMMIT:path/to/suspect.cs"
git log --oneline --decorate "$COMMIT" -- path/to/suspect.cs
```

For multi-file investigation, create a detached temporary worktree outside the workspace:

```zsh
SOURCE_DIR=$(mktemp -d "$TMPDIR/masjid-source.XXXXXX")
rmdir "$SOURCE_DIR"
git worktree add --detach "$SOURCE_DIR" "$COMMIT"
```

Remove it when finished:

```zsh
git worktree remove "$SOURCE_DIR"
```

Account for `metadata/version-bump.diff`: the publish script may have changed version files after checkout, while the manifest commit still identifies the code revision.

Check release history around the crashing build:

```zsh
git log --date=iso --format='%h %ad %s' --all --since='<time>' --until='<time>'
git diff --stat <previous-good-commit>..<manifest-commit>
```

Prefer the narrowest path supported by symbolized frames, a newly shipped callback, or a neighboring failure-handling change. Do not search every `async` method merely because the crash is a `SIGABRT`.

### 8. Form And Test One Hypothesis

State one falsifiable hypothesis containing:

- the exact app-owned entry point or callback;
- the operation likely to throw/assert;
- why the crash timing/thread/frames support it;
- one cheap check that could disconfirm it.

Examples of discriminating checks:

- Reproduce the exact action on the same iOS major version while attached to the debugger.
- Deny notification permission, then exercise the symbolized scheduling callback.
- Compare build N against the prior known-good build only along the symbolized path.
- Inspect telemetry/device console for the same incident timestamp.
- Verify whether an `async void` event or `AsyncRelayCommand` lets the exception escape to the UI synchronization context.

When hardening an external/platform boundary:

- catch at the owning service boundary if every caller needs graceful degradation;
- log the original exception;
- return a typed failure result or non-blocking state;
- rethrow caller-requested `OperationCanceledException`;
- avoid broad catches that hide programming errors outside the external boundary.

### 9. Validate A Fix

Follow the repository definition of done:

```zsh
dotnet test tests/MasjidApp.Core.Tests/MasjidApp.Core.Tests.csproj -c Debug
dotnet build src/MasjidApp/MasjidApp.csproj \
  -c Debug -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 \
  -warnaserror --no-restore
dotnet test MasjidApp.slnx -c Debug --no-restore
dotnet build MasjidApp.slnx -c Debug -warnaserror --no-restore
```

Also reproduce the relevant device flow when the failure depends on iOS permissions, notification APIs, signing, entitlements, or release-only AOT behavior. A simulator Debug build cannot prove a TestFlight-only Release/device issue is fixed.

## Decision Table

| Evidence | Next action |
| --- | --- |
| Checksums fail | Redownload; do not symbolicate. |
| Version/build mismatch | Select another workflow run. |
| UUID mismatch | Select another artifact/attempt; never rebuild as a substitute. |
| Artifact expired | Recover original symbols from durable storage or App Store Connect; otherwise report blocked. |
| Only `ios-publish-diagnostics-*` exists | The run did not produce an archive and cannot explain a later runtime crash. |
| Exact dSYM yields app-owned frame | Trace that method at the manifest commit. |
| Exact dSYM yields only Mono abort frames | Seek managed runtime logs/telemetry and correlate release changes; label cause as hypothesis. |
| Native assertion appears | Inspect the invoking app callback and Apple API preconditions/threading. |
| Crash predates artifact retention | State that exact symbolication is unavailable; preserve the report and improve observability for the next build. |

## Completion Report

Report in this order:

1. **Confirmed:** incident/build, UUID match, symbolized frames, thread, and termination mechanism.
2. **Likely cause:** narrow code path and evidence, clearly labeled with confidence.
3. **Unknown:** missing managed exception/logs or other unavailable evidence.
4. **Change:** files/behavior changed, if a fix was requested.
5. **Validation:** exact automated and device checks run, plus anything not run.
6. **Artifact provenance:** workflow run ID, artifact name, manifest commit, and dSYM UUID.

Never claim root cause solely because a later commit's description sounds related. History is supporting evidence; exact symbols, runtime logs, or a controlled reproduction carry more weight.
