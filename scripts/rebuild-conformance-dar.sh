#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 --package <dir-under-conformance> --base <base-name>

Rebuilds a conformance fixture's committed DAR and everything derived from it,
in one command, after a change to conformance/<package>'s Daml sources or
daml.yaml:

  1. scripts/set-conformance-package-name.sh   re-derives the content-addressed
                                               package name in daml.yaml
  2. dpm build                                 compiles the package
  3. cp .daml/dist/<name>-*.dar <package>.dar  replaces the committed DAR
  4. scripts/refresh-conformance.sh            regenerates the shipped bindings

Commit the changed daml.yaml, DAR and generated tree together.
ConformancePackageIdentityDriftTests fails when a step was skipped.

Requires 'dpm' on PATH, plus everything scripts/refresh-conformance.sh needs
(java, a built Daml.Codegen.CSharp.Cli).
EOF
  exit "${1:-1}"
}

PACKAGE=""
BASE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --package) PACKAGE="$2"; shift 2 ;;
    --base) BASE="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "rebuild-conformance-dar.sh: unknown arg: $1" >&2; usage ;;
  esac
done
[[ -n "$PACKAGE" && -n "$BASE" ]] || usage

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PACKAGE_DIR="$PROJECT_ROOT/conformance/$PACKAGE"

[[ -d "$PACKAGE_DIR" ]] || { echo "rebuild-conformance-dar.sh: no such package directory: $PACKAGE_DIR" >&2; exit 1; }
command -v dpm >/dev/null 2>&1 || { echo "rebuild-conformance-dar.sh: 'dpm' not found on PATH" >&2; exit 1; }

PACKAGE_NAME="$("$SCRIPT_DIR/set-conformance-package-name.sh" --package "$PACKAGE" --base "$BASE")"

(cd "$PACKAGE_DIR" && dpm build)

BUILT_DARS=("$PACKAGE_DIR"/.daml/dist/"$PACKAGE_NAME"-*.dar)
[[ ${#BUILT_DARS[@]} -eq 1 && -f "${BUILT_DARS[0]}" ]] || {
  echo "rebuild-conformance-dar.sh: expected exactly one DAR matching $PACKAGE_DIR/.daml/dist/$PACKAGE_NAME-*.dar, found: ${BUILT_DARS[*]}" >&2
  exit 1
}
cp "${BUILT_DARS[0]}" "$PACKAGE_DIR/$PACKAGE.dar"

"$SCRIPT_DIR/refresh-conformance.sh" --package "$PACKAGE"

echo ""
echo "Rebuilt $PACKAGE_NAME."
