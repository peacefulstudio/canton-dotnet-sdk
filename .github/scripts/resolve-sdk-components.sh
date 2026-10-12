#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 <daml-sdk-version>

Look up the damlc and daml-script component versions that a Daml SDK release
ships, from Digital Asset's SDK manifest
(europe-docker.pkg.dev/da-images/public/sdk-manifests/open-source:<sdk>), and
export them to \$GITHUB_ENV as DAMLC_VERSION and DAML_SCRIPT_VERSION.

Fetches the manifest with oras when it is on PATH, otherwise with
'dpm install <sdk>' and its cache. SDK_MANIFEST_DIR, when set, names a
directory holding <sdk>.yaml and skips the fetch.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0
[ $# -eq 1 ] || usage 1

SDK_VERSION="$1"
if ! [[ "$SDK_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
  echo "::error::daml_sdk_version='$SDK_VERSION' must match ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$"; exit 1
fi

fetch_manifest() {
  local destination="$1"
  if [ -n "${SDK_MANIFEST_DIR:-}" ]; then
    cp "$SDK_MANIFEST_DIR/$SDK_VERSION.yaml" "$destination"
  elif command -v oras >/dev/null 2>&1; then
    local pull_dir
    pull_dir="$(mktemp -d)"
    oras pull "europe-docker.pkg.dev/da-images/public/sdk-manifests/open-source:$SDK_VERSION" -o "$pull_dir" --no-tty >&2
    cp "$pull_dir/$SDK_VERSION.yaml" "$destination"
    rm -rf "$pull_dir"
  else
    dpm install "$SDK_VERSION" >&2
    cp "$HOME/.dpm/cache/sdk/open-source/$SDK_VERSION.yaml" "$destination"
  fi
}

component_version() {
  awk -v component="$1" '
    /^  components:/ { in_components = 1; next }
    /^  [a-z]/ { in_components = 0 }
    in_components && /^    [a-z]/ { current = $1; sub(/:$/, "", current) }
    in_components && current == component && $1 == "version:" { print $2; exit }
  ' "$MANIFEST"
}

MANIFEST="$(mktemp)"
trap 'rm -f "$MANIFEST"' EXIT
fetch_manifest "$MANIFEST"

DAMLC_VERSION="$(component_version damlc)"
DAML_SCRIPT_VERSION="$(component_version daml-script)"
for pair in "damlc=$DAMLC_VERSION" "daml-script=$DAML_SCRIPT_VERSION"; do
  [ -n "${pair#*=}" ] || { echo "::error::SDK $SDK_VERSION manifest lists no '${pair%%=*}' component"; exit 1; }
done

if [ -n "${GITHUB_ENV:-}" ]; then
  {
    echo "DAMLC_VERSION=$DAMLC_VERSION"
    echo "DAML_SCRIPT_VERSION=$DAML_SCRIPT_VERSION"
  } >> "$GITHUB_ENV"
fi
echo "SDK $SDK_VERSION ships damlc=$DAMLC_VERSION daml-script=$DAML_SCRIPT_VERSION"
