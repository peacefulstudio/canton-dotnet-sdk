#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 --package <dir-under-conformance> --base <base-name> [--print]

Rewrites conformance/<package>/daml.yaml's 'name:' line to a content-addressed
package name '<base-name>-h<hash12>'. The 'h' prefix is required: damlc's
package-name grammar requires every dash-separated segment to start with a
letter (^[A-Za-z][A-Za-z0-9]*(-[A-Za-z][A-Za-z0-9]*)*$), and a bare hex digest
can start with a digit.

--print computes and prints the expected name without writing daml.yaml — a
CI check (or another script) uses this to verify the committed name is still
the one the current sources hash to, catching a source edit that skipped this
script before rebuilding.

hash12 is the first 12 hex characters of the SHA-256 over:
  - every *.daml file under conformance/<package> (path relative to the
    package dir, then its bytes), in sorted path order
  - daml.yaml with its 'name:' line removed

Excluding the name line makes the hash independent of its own output, so
re-running this script is idempotent when nothing else changed, and moves
the name only when the compiled package's content actually would. The
sdk-version and build-options that also determine the compiled package are
covered because they live in daml.yaml alongside everything else in the file.

Canton's upgrade check (and 'dpm build''s KNOWN_PACKAGE_VERSION rejection)
works per package name, so two builds with different content need different
names. Package version is untouched.

Run 'dpm build' after this to produce a DAR whose main package id reflects
the new name, then scripts/refresh-conformance.sh to regenerate the bindings.
EOF
  exit "${1:-1}"
}

PACKAGE=""
BASE=""
PRINT_ONLY=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --package) PACKAGE="$2"; shift 2 ;;
    --base) BASE="$2"; shift 2 ;;
    --print) PRINT_ONLY=1; shift ;;
    -h|--help) usage 0 ;;
    *) echo "set-conformance-package-name.sh: unknown arg: $1" >&2; usage ;;
  esac
done
[[ -n "$PACKAGE" && -n "$BASE" ]] || usage

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PACKAGE_DIR="$PROJECT_ROOT/conformance/$PACKAGE"
YAML="$PACKAGE_DIR/daml.yaml"

[[ -d "$PACKAGE_DIR" ]] || { echo "set-conformance-package-name.sh: no such package directory: $PACKAGE_DIR" >&2; exit 1; }
[[ -f "$YAML" ]] || { echo "set-conformance-package-name.sh: no daml.yaml at $YAML" >&2; exit 1; }
grep -q '^name:' "$YAML" || { echo "set-conformance-package-name.sh: $YAML has no 'name:' line" >&2; exit 1; }

sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum
  else
    shasum -a 256
  fi
}

HASH12="$(
  {
    find "$PACKAGE_DIR" -type f -name '*.daml' -print | LC_ALL=C sort | while IFS= read -r f; do
      printf '%s\n' "${f#"$PACKAGE_DIR"/}"
      cat "$f"
    done
    grep -v '^name:' "$YAML"
  } | sha256_stream | awk '{print $1}' | cut -c1-12
)"

NEW_NAME="${BASE}-h${HASH12}"

if [[ "$PRINT_ONLY" -eq 1 ]]; then
  echo "$NEW_NAME"
  exit 0
fi

sed -i.bak "s/^name:.*/name: $NEW_NAME/" "$YAML"
rm -f "$YAML.bak"

echo "$NEW_NAME"
