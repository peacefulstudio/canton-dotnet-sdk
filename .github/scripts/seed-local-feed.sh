#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Seed the local NuGet feed with the core packages attached to the release, then
assert every core package the generated DAR projects restore is available at
RUNTIME_VERSION: either as a nupkg in the local feed, or already indexed on the
nuget.org flat container. A DAR-only republish against an already-indexed core
version therefore keeps passing when the release carries no core nupkgs.

The core packages are the ones ProjectFileGenerator emits as PackageReferences
into every generated csproj, both pinned to the runtime version:
  Daml.Runtime, Daml.Ledger.Abstractions

A failed release download is not fatal on its own: it is reported, and the run
fails with an ::error:: annotation only when a core package is then unavailable.

Reads (env):
  GH_TOKEN            Token for gh release download.
  GITHUB_REPOSITORY   owner/repo holding the release.
  RELEASE_TAG         Release tag whose nupkg assets seed the feed.
  RUNTIME_VERSION     Resolved Daml.Runtime version the DAR projects restore.
  RUNNER_TEMP         Parent of the local feed directory (local-feed).
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

CORE_PACKAGE_IDS=(Daml.Runtime Daml.Ledger.Abstractions)
FLAT_CONTAINER="https://api.nuget.org/v3-flatcontainer"

if [ -z "${RUNTIME_VERSION:-}" ]; then
  echo "::error::RUNTIME_VERSION is empty; cannot tell which core package version the DAR projects restore"
  exit 1
fi

FEED="$RUNNER_TEMP/local-feed"
mkdir -p "$FEED"

download_failed=false
if gh release download "$RELEASE_TAG" --repo "$GITHUB_REPOSITORY" --pattern '*.nupkg' --dir "$FEED" --clobber; then
  echo "Seeded the local feed from $RELEASE_TAG:"
  ls -1 "$FEED"
else
  download_failed=true
  echo "Could not seed the local feed from $RELEASE_TAG; core packages must already be indexed on nuget.org."
fi

metadata_free_version="${RUNTIME_VERSION%%+*}"
indexed_version="$(printf '%s' "$metadata_free_version" | tr '[:upper:]' '[:lower:]')"

is_indexed_on_nuget_org() {
  local id_lowercase
  id_lowercase="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  curl -fsS --retry 3 "$FLAT_CONTAINER/$id_lowercase/index.json" \
    | jq -e --arg version "$indexed_version" '.versions | index($version) != null' > /dev/null
}

unavailable=()
for id in "${CORE_PACKAGE_IDS[@]}"; do
  if [ -f "$FEED/$id.$metadata_free_version.nupkg" ]; then
    echo "$id $RUNTIME_VERSION: in the local feed"
  elif is_indexed_on_nuget_org "$id" 2> /dev/null; then
    echo "$id $RUNTIME_VERSION: already indexed on nuget.org"
  else
    unavailable+=("$id $RUNTIME_VERSION")
  fi
done

if [ "${#unavailable[@]}" -gt 0 ]; then
  cause="the release $RELEASE_TAG carries no such nupkg"
  [ "$download_failed" = "true" ] && cause="the release download from $RELEASE_TAG failed"
  for package in "${unavailable[@]}"; do
    echo "::error::$package is neither in the local feed nor indexed on nuget.org ($cause); the DAR projects cannot restore it"
  done
  exit 1
fi
