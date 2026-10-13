#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 <daml-sdk-version>

Look up the damlc and daml-script component versions that a Daml SDK release
ships, from Digital Asset's SDK manifest
(europe-docker.pkg.dev/da-images/public/sdk-manifests/open-source), and export
them to \$GITHUB_ENV with the digests versions.yaml pins them at:

  DAMLC_VERSION, DAML_SCRIPT_VERSION, DAMLC_DIGEST, DAML_SCRIPT_DIGEST

The manifest is fetched by the digest versions.yaml pins as daml-sdk-sha256, and
the script fails unless that manifest is the one of <daml-sdk-version> and the
damlc and daml-script tags it names still resolve to the digests versions.yaml
pins as damlc-sha256 and daml-script-sha256. A missing key, a malformed digest or
a digest that does not resolve is an error, never a fallback to a tag.

Needs oras on PATH. VERSIONS_FILE, when set, names the versions.yaml to read
instead of the one at the repository root.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0
[ $# -eq 1 ] || usage 1

SDK_VERSION="$1"
if ! [[ "$SDK_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
  echo "::error::daml_sdk_version='$SDK_VERSION' must match ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$"; exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSIONS_FILE="${VERSIONS_FILE:-$SCRIPT_DIR/../../versions.yaml}"
MANIFEST_REPOSITORY="europe-docker.pkg.dev/da-images/public/sdk-manifests/open-source"
COMPONENT_REPOSITORY="europe-docker.pkg.dev/da-images/public/components"

pinned_digest() {
  local key="$1" digest
  digest="$(awk -F': *' -v key="$key" '$1 == key { gsub(/["'"'"' ]/, "", $2); print $2 }' "$VERSIONS_FILE")"
  [ -n "$digest" ] || { echo "::error::versions.yaml has no '$key' entry" >&2; exit 1; }
  [[ "$digest" =~ ^[0-9a-f]{64}$ ]] || { echo "::error::$key must be 64 lowercase hex digits, without a sha256: prefix" >&2; exit 1; }
  echo "$digest"
}

SDK_MANIFEST_SHA256="$(pinned_digest daml-sdk-sha256)"
DAMLC_SHA256="$(pinned_digest damlc-sha256)"
DAML_SCRIPT_SHA256="$(pinned_digest daml-script-sha256)"

command -v oras >/dev/null 2>&1 || { echo "::error::oras is required on PATH to fetch the SDK manifest"; exit 1; }

PULL_DIR="$(mktemp -d)"
trap 'rm -rf "$PULL_DIR"' EXIT

oras pull "$MANIFEST_REPOSITORY@sha256:$SDK_MANIFEST_SHA256" -o "$PULL_DIR" --no-tty >&2 \
  || { echo "::error::the SDK manifest digest sha256:$SDK_MANIFEST_SHA256 (daml-sdk-sha256) does not resolve"; exit 1; }

MANIFEST="$PULL_DIR/$SDK_VERSION.yaml"
[ -f "$MANIFEST" ] || { echo "::error::the manifest at daml-sdk-sha256 carries no $SDK_VERSION.yaml; the digest is pinned for another SDK"; exit 1; }

DECLARED_SDK_VERSION="$(awk '/^  version:/ { print $2; exit }' "$MANIFEST")"
[ "$DECLARED_SDK_VERSION" = "$SDK_VERSION" ] \
  || { echo "::error::the manifest at daml-sdk-sha256 declares SDK $DECLARED_SDK_VERSION, not $SDK_VERSION"; exit 1; }

component_version() {
  awk -v component="$1" '
    /^  components:/ { in_components = 1; next }
    /^  [a-z]/ { in_components = 0 }
    in_components && /^    [a-z]/ { current = $1; sub(/:$/, "", current) }
    in_components && current == component && $1 == "version:" { print $2; exit }
  ' "$MANIFEST"
}

assert_tag_resolves_to_pin() {
  local component="$1" version="$2" pinned_sha256="$3" resolved
  resolved="$(oras resolve "$COMPONENT_REPOSITORY/$component:$version" 2>/dev/null)" \
    || { echo "::error::$component $version does not resolve"; exit 1; }
  [ "$resolved" = "sha256:$pinned_sha256" ] \
    || { echo "::error::$component $version resolves to $resolved, not the pinned sha256:$pinned_sha256 ($component-sha256 in versions.yaml is stale)"; exit 1; }
}

DAMLC_VERSION="$(component_version damlc)"
DAML_SCRIPT_VERSION="$(component_version daml-script)"
for pair in "damlc=$DAMLC_VERSION" "daml-script=$DAML_SCRIPT_VERSION"; do
  [ -n "${pair#*=}" ] || { echo "::error::SDK $SDK_VERSION manifest lists no '${pair%%=*}' component"; exit 1; }
done

assert_tag_resolves_to_pin damlc "$DAMLC_VERSION" "$DAMLC_SHA256"
assert_tag_resolves_to_pin daml-script "$DAML_SCRIPT_VERSION" "$DAML_SCRIPT_SHA256"

if [ -n "${GITHUB_ENV:-}" ]; then
  {
    echo "DAMLC_VERSION=$DAMLC_VERSION"
    echo "DAML_SCRIPT_VERSION=$DAML_SCRIPT_VERSION"
    echo "DAMLC_DIGEST=sha256:$DAMLC_SHA256"
    echo "DAML_SCRIPT_DIGEST=sha256:$DAML_SCRIPT_SHA256"
  } >> "$GITHUB_ENV"
fi
echo "SDK $SDK_VERSION ships damlc=$DAMLC_VERSION daml-script=$DAML_SCRIPT_VERSION"
