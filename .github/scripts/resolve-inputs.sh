#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Resolve and validate the effective publish inputs, defaulting unset values from
Directory.Build.props <Version>, and export the resolved values to \$GITHUB_ENV.

Reads (env):
  RUNTIME_VERSION_INPUT     Daml.Runtime version; empty => Directory.Build.props <Version>.
  CODEGEN_CS_VERSION_INPUT  codegen-cs version; empty => Directory.Build.props <Version>.
  DAMLC_VERSION_INPUT       damlc component version (required).
  PACKAGE_LICENSE_INPUT     SPDX license expression (default: Apache-2.0).
  DRY_RUN_INPUT             dry-run flag, passed through verbatim.
  SOURCE_LABEL              Short label used in the resolved-summary line.

Writes (when set):
  GITHUB_ENV  RUNTIME_VERSION, CODEGEN_CS_VERSION, DAMLC_VERSION,
              PACKAGE_LICENSE, DRY_RUN.
EOF
  exit "${1:-1}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

EFFECTIVE_RUNTIME="${RUNTIME_VERSION_INPUT:-}"
if [ -z "$EFFECTIVE_RUNTIME" ]; then
  # `|| true` works around SIGPIPE killing `grep` when `head -1` closes the pipe.
  EFFECTIVE_RUNTIME=$(grep -oP '(?<=<Version>)[^<]+' "$REPO_ROOT/Directory.Build.props" | head -1 || true)
fi
EFFECTIVE_CODEGEN_CS="${CODEGEN_CS_VERSION_INPUT:-}"
if [ -z "$EFFECTIVE_CODEGEN_CS" ]; then
  EFFECTIVE_CODEGEN_CS=$(grep -oP '(?<=<Version>)[^<]+' "$REPO_ROOT/Directory.Build.props" | head -1 || true)
fi
EFFECTIVE_DAMLC="${DAMLC_VERSION_INPUT:-}"
EFFECTIVE_PACKAGE_LICENSE="${PACKAGE_LICENSE_INPUT:-Apache-2.0}"
EFFECTIVE_DRY_RUN="${DRY_RUN_INPUT:-}"

if ! [[ "$EFFECTIVE_RUNTIME" =~ ^[A-Za-z0-9.+-]+$ ]]; then
  echo "::error::runtime_version='$EFFECTIVE_RUNTIME' must be a NuGet version string"; exit 1
fi
if ! [[ "$EFFECTIVE_CODEGEN_CS" =~ ^[A-Za-z0-9.+-]+$ ]]; then
  echo "::error::codegen_cs_version='$EFFECTIVE_CODEGEN_CS' must be a valid version string"; exit 1
fi
if ! [[ "$EFFECTIVE_DAMLC" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
  echo "::error::damlc_version='$EFFECTIVE_DAMLC' must match ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$"; exit 1
fi
if ! [[ "$EFFECTIVE_PACKAGE_LICENSE" =~ ^[A-Za-z0-9.+()-]+([[:blank:]]+(AND|OR|WITH)[[:blank:]]+[A-Za-z0-9.+()-]+)*$ ]]; then
  echo "::error::package_license='$EFFECTIVE_PACKAGE_LICENSE' must be a valid SPDX expression"; exit 1
fi

if [ -n "${GITHUB_ENV:-}" ]; then
  {
    echo "RUNTIME_VERSION=$EFFECTIVE_RUNTIME"
    echo "CODEGEN_CS_VERSION=$EFFECTIVE_CODEGEN_CS"
    echo "DAMLC_VERSION=$EFFECTIVE_DAMLC"
    echo "PACKAGE_LICENSE=$EFFECTIVE_PACKAGE_LICENSE"
    echo "DRY_RUN=$EFFECTIVE_DRY_RUN"
  } >> "$GITHUB_ENV"
fi
echo "Resolved [${SOURCE_LABEL:-}]: runtime=$EFFECTIVE_RUNTIME codegen-cs=$EFFECTIVE_CODEGEN_CS damlc=$EFFECTIVE_DAMLC license=$EFFECTIVE_PACKAGE_LICENSE dry_run=$EFFECTIVE_DRY_RUN"
