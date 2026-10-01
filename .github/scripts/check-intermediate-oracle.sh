#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF2
Usage: $0 <daml-dar-to-proto.jar>

Decode conformance/richtypes/richtypes.dar with the given helper jar and fail
unless the sha256 of the resulting Intermediate DAR equals the checksum
committed in tests/determinism/shas/intermediate.sha256.
EOF2
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0
[ $# -eq 1 ] || usage 1

JAR="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ORACLE_FILE="$REPO_ROOT/tests/determinism/shas/intermediate.sha256"

[ -f "$JAR" ] || { echo "::error::helper jar not found at $JAR"; exit 1; }
[ -f "$ORACLE_FILE" ] || { echo "::error::determinism oracle missing at $ORACLE_FILE"; exit 1; }

EXPECTED="$(awk 'NF { print $1; exit }' "$ORACLE_FILE")"
if ! [[ "$EXPECTED" =~ ^[0-9a-f]{64}$ ]]; then
  echo "::error::$ORACLE_FILE does not start with a sha256 hex digest"; exit 1
fi

OUT="$(mktemp -d)/richtypes-full-decode.binpb"
java -jar "$JAR" --dar "$REPO_ROOT/conformance/richtypes/richtypes.dar" --out "$OUT"
if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL="$(sha256sum "$OUT" | awk '{print $1}')"
else
  ACTUAL="$(shasum -a 256 "$OUT" | awk '{print $1}')"
fi

if [ "$ACTUAL" != "$EXPECTED" ]; then
  echo "::error::richtypes.dar full-decode sha256 ($ACTUAL) does not match tests/determinism/shas/intermediate.sha256 ($EXPECTED) — the helper jar's Intermediate DAR output has drifted from the determinism oracle."
  exit 1
fi
echo "richtypes.dar full-decode matches the determinism oracle: $ACTUAL"
