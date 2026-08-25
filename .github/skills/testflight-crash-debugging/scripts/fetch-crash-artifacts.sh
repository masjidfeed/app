#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  fetch-crash-artifacts.sh <crash-report> [options]

Downloads and verifies the exact TestFlight publish artifact for an Apple crash.

Options:
  --run-id ID             Restrict artifact lookup to one workflow run.
  --artifact-name NAME    Select an exact GitHub Actions artifact name.
  --artifact-dir DIR      Verify an already-downloaded artifact instead of GitHub.
  --output DIR            Case directory. Defaults to a new directory under $TMPDIR.
  --version VERSION       Override the report's marketing version.
  --build BUILD           Override the report's build number.
  --uuid UUID             Override the crashed MasjidApp image UUID.
  -h, --help              Show this help.

The report parser understands standard Apple .crash reports. For .ips variants whose
JSON shape cannot be parsed, pass --version, --build, and --uuid explicitly.
EOF
}

fail() {
  printf 'error: %s\n' "$*" >&2
  exit 1
}

require_value() {
  local option="$1" value="${2:-}"
  [ -n "$value" ] || fail "$option requires a value"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

normalize_uuid() {
  printf '%s' "$1" | tr -d '<>-' | tr '[:lower:]' '[:upper:]'
}

normalize_uuid_list() {
  tr -d '-' | tr '[:lower:]' '[:upper:]' | sort
}

manifest_value() {
  local key="$1" manifest="$2"
  sed -n "s/^${key}=//p" "$manifest" | sed -n '1p'
}

crash_report=""
run_id=""
artifact_name=""
local_artifact_dir=""
output_dir=""
crash_version=""
crash_build=""
crash_uuid=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --run-id)
      require_value "$1" "${2:-}"
      run_id="$2"
      shift 2
      ;;
    --artifact-name)
      require_value "$1" "${2:-}"
      artifact_name="$2"
      shift 2
      ;;
    --artifact-dir)
      require_value "$1" "${2:-}"
      local_artifact_dir="$2"
      shift 2
      ;;
    --output)
      require_value "$1" "${2:-}"
      output_dir="$2"
      shift 2
      ;;
    --version)
      require_value "$1" "${2:-}"
      crash_version="$2"
      shift 2
      ;;
    --build)
      require_value "$1" "${2:-}"
      crash_build="$2"
      shift 2
      ;;
    --uuid)
      require_value "$1" "${2:-}"
      crash_uuid="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --*)
      fail "unknown option: $1"
      ;;
    *)
      [ -z "$crash_report" ] || fail "only one crash report may be supplied"
      crash_report="$1"
      shift
      ;;
  esac
done

[ -n "$crash_report" ] || {
  usage >&2
  exit 2
}
[ -f "$crash_report" ] || fail "crash report not found: $crash_report"

require_command ditto
require_command shasum
require_command xcrun

if [ -z "$crash_version" ]; then
  crash_version=$(sed -n 's/^Version:[[:space:]]*\([^[:space:]]*\).*/\1/p' "$crash_report" | sed -n '1p')
fi
if [ -z "$crash_build" ]; then
  crash_build=$(sed -n 's/^Version:.*(\([^)]*\)).*/\1/p' "$crash_report" | sed -n '1p')
fi
if [ -z "$crash_uuid" ]; then
  crash_uuid=$(
    sed -n '/^Binary Images:/,$p' "$crash_report" |
      sed -n '/MasjidApp arm64/{s/.*<\([^>]*\)>.*/\1/p;q;}'
  )
fi
crash_uuid=$(normalize_uuid "$crash_uuid")
[ -n "$crash_uuid" ] || fail "could not parse MasjidApp UUID; pass --uuid"

case_root="${TMPDIR:-/tmp}"
if [ -n "$output_dir" ]; then
  case_dir="$output_dir"
  if [ -d "$case_dir" ] && [ -n "$(find "$case_dir" -mindepth 1 -print -quit)" ]; then
    fail "output directory is not empty: $case_dir"
  fi
  mkdir -p "$case_dir"
else
  case_dir=$(mktemp -d "$case_root/masjid-crash.XXXXXX")
fi
case_dir=$(cd "$case_dir" && pwd)

if [ -n "$local_artifact_dir" ]; then
  [ -d "$local_artifact_dir" ] || fail "artifact directory not found: $local_artifact_dir"
  artifact_dir=$(cd "$local_artifact_dir" && pwd)
  selected_name=$(basename "$artifact_dir")
  selected_run="local"
else
  require_command gh
  gh auth status >/dev/null
  repo=$(gh repo view --json nameWithOwner --jq '.nameWithOwner')
  [ -n "$repo" ] || fail "could not determine GitHub repository"

  [ -n "$crash_version" ] || fail "could not parse crash version; pass --version"
  [ -n "$crash_build" ] || fail "could not parse crash build; pass --build"
  prefix="ios-crash-debug-${crash_version}-${crash_build}-"

  candidates="$case_dir/artifact-candidates.tsv"
  gh api --paginate "repos/$repo/actions/artifacts?per_page=100" \
    --jq '.artifacts[] | select(.expired == false) | [.id, .name, .workflow_run.id, .created_at] | @tsv' \
    > "$candidates"

  filtered="$case_dir/artifact-candidates-filtered.tsv"
  awk -F '\t' \
    -v prefix="$prefix" \
    -v exact="$artifact_name" \
    -v wanted_run="$run_id" '
      (exact == "" ? index($2, prefix) == 1 : $2 == exact) &&
      (wanted_run == "" || $3 == wanted_run) { print }
    ' "$candidates" | sort -t "$(printf '\t')" -k4,4r > "$filtered"

  candidate=$(sed -n '1p' "$filtered")
  [ -n "$candidate" ] || fail "no non-expired crash artifact matches version $crash_version build $crash_build"

  artifact_id=$(printf '%s\n' "$candidate" | cut -f1)
  selected_name=$(printf '%s\n' "$candidate" | cut -f2)
  selected_run=$(printf '%s\n' "$candidate" | cut -f3)
  [ -n "$artifact_id" ] || fail "selected artifact has no ID"

  artifact_dir="$case_dir/artifact"
  mkdir -p "$artifact_dir"
  printf 'Downloading %s from workflow run %s...\n' "$selected_name" "$selected_run"
  gh run download "$selected_run" --name "$selected_name" --dir "$artifact_dir"
fi

checksums="$artifact_dir/CHECKSUMS.sha256"
manifest="$artifact_dir/metadata/build-manifest.txt"
[ -f "$checksums" ] || fail "artifact has no CHECKSUMS.sha256"
[ -f "$manifest" ] || fail "artifact has no metadata/build-manifest.txt"

printf 'Verifying retained artifact checksums...\n'
(
  cd "$artifact_dir"
  shasum -a 256 -c CHECKSUMS.sha256
)

manifest_version=$(manifest_value app_version "$manifest")
manifest_build=$(manifest_value build_number "$manifest")
manifest_commit=$(manifest_value commit "$manifest")
manifest_uuid=$(normalize_uuid "$(manifest_value app_uuid "$manifest")")

if [ -n "$crash_version" ] && [ "$manifest_version" != "$crash_version" ]; then
  fail "manifest version $manifest_version does not match crash version $crash_version"
fi
if [ -n "$crash_build" ] && [ "$manifest_build" != "$crash_build" ]; then
  fail "manifest build $manifest_build does not match crash build $crash_build"
fi
[ "$manifest_uuid" = "$crash_uuid" ] || fail "manifest UUID $manifest_uuid does not match crash UUID $crash_uuid"

archive_zip=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.xcarchive.zip' -print -quit)
dsym_zip=$(find "$artifact_dir" -maxdepth 1 -type f -name '*.dSYMs.zip' -print -quit)
[ -n "$archive_zip" ] || fail "artifact has no xcarchive zip"
[ -n "$dsym_zip" ] || fail "artifact has no dSYM zip"

archive_out="$case_dir/unpacked/archive"
dsym_out="$case_dir/unpacked/dsyms"
mkdir -p "$archive_out" "$dsym_out"
ditto -x -k "$archive_zip" "$archive_out"
ditto -x -k "$dsym_zip" "$dsym_out"

archive=$(find "$archive_out" -type d -name '*.xcarchive' -print -quit)
dsym=$(find "$dsym_out" -type d -name 'MasjidApp.app.dSYM' -print -quit)
[ -n "$archive" ] || fail "extracted xcarchive not found"
[ -n "$dsym" ] || fail "extracted MasjidApp.app.dSYM not found"

binary="$archive/Products/Applications/MasjidApp.app/MasjidApp"
dwarf="$dsym/Contents/Resources/DWARF/MasjidApp"
[ -f "$binary" ] || fail "extracted app binary not found"
[ -f "$dwarf" ] || fail "extracted dSYM DWARF not found"

binary_uuids=$(xcrun dwarfdump --uuid "$binary" | awk '{print $2}' | normalize_uuid_list)
dsym_uuids=$(xcrun dwarfdump --uuid "$dwarf" | awk '{print $2}' | normalize_uuid_list)
[ "$binary_uuids" = "$dsym_uuids" ] || fail "extracted app and dSYM UUIDs differ"
printf '%s\n' "$binary_uuids" | grep -qx "$crash_uuid" || fail "extracted symbols do not contain crash UUID $crash_uuid"

summary="$case_dir/case-summary.txt"
{
  echo "crash_report=$crash_report"
  echo "crash_version=${crash_version:-$manifest_version}"
  echo "crash_build=${crash_build:-$manifest_build}"
  echo "crash_uuid=$crash_uuid"
  echo "artifact_name=$selected_name"
  echo "workflow_run_id=$selected_run"
  echo "manifest_commit=$manifest_commit"
  echo "artifact_dir=$artifact_dir"
  echo "archive=$archive"
  echo "binary=$binary"
  echo "dsym=$dsym"
  echo "dwarf=$dwarf"
} > "$summary"

cat "$summary"
printf '\nVerified exact crash symbols. Case directory: %s\n' "$case_dir"
printf 'Use this DWARF with atos: %s\n' "$dwarf"
