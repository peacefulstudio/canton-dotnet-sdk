#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOT
Usage: $0 <codegen-cs-version> <built-index-digest> <dry-run>

Decide the digest the DAR packing legs pull ghcr.io/peacefulstudio/dpm-codegen-cs at,
and write it to \$GITHUB_OUTPUT as digest=sha256:<hex>.

  dry-run false  the digest of the index oci-build pushed in this run; empty or malformed
                 is an error.
  dry-run true   oci-build pushes nothing, so the digest the already-published
                 <codegen-cs-version> tag resolves to now. That path is not pinned to a
                 build output; a tag that does not resolve is an error.

Needs oras on PATH for a dry run.
EOT
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0
[ $# -eq 3 ] || usage 1

CODEGEN_CS_VERSION="$1"
BUILT_DIGEST="$2"
DRY_RUN="$3"
IMAGE="ghcr.io/peacefulstudio/dpm-codegen-cs"
DIGEST_PATTERN='^sha256:[0-9a-f]{64}$'

if [ "$DRY_RUN" = "true" ]; then
  command -v oras >/dev/null 2>&1 || { echo "::error::oras is required on PATH to resolve the published codegen-cs tag"; exit 1; }
  DIGEST="$(oras resolve "$IMAGE:$CODEGEN_CS_VERSION" 2>/dev/null)" \
    || { echo "::error::$IMAGE:$CODEGEN_CS_VERSION does not resolve; a dry run needs an already-published codegen_cs_version"; exit 1; }
  echo "Dry run: $IMAGE:$CODEGEN_CS_VERSION is $DIGEST, resolved now by tag; not pinned to a build output."
else
  [[ "$BUILT_DIGEST" =~ $DIGEST_PATTERN ]] \
    || { echo "::error::oci-build pushed no index digest ('$BUILT_DIGEST'); refusing to pull codegen-cs by tag"; exit 1; }
  DIGEST="$BUILT_DIGEST"
  echo "$IMAGE:$CODEGEN_CS_VERSION is pinned to the digest oci-build pushed: $DIGEST."
fi

[[ "$DIGEST" =~ $DIGEST_PATTERN ]] || { echo "::error::'$DIGEST' is not a sha256 digest"; exit 1; }
echo "digest=$DIGEST" >> "${GITHUB_OUTPUT:-/dev/null}"
