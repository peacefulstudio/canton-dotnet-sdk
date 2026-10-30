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
  DAML_SDK_VERSION_INPUT    Daml SDK version (required).
  PACKAGE_LICENSE_INPUT     SPDX license expression (default: Apache-2.0).
  DRY_RUN_INPUT             dry-run flag, passed through verbatim.
  SOURCE_LABEL              Short label used in the resolved-summary line.

Writes (when set):
  GITHUB_ENV  RUNTIME_VERSION, CODEGEN_CS_VERSION, DAML_SDK_VERSION,
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
EFFECTIVE_DAML_SDK="${DAML_SDK_VERSION_INPUT:-}"
EFFECTIVE_PACKAGE_LICENSE="${PACKAGE_LICENSE_INPUT:-Apache-2.0}"
EFFECTIVE_DRY_RUN="${DRY_RUN_INPUT:-}"

NUMERIC_COMPONENT='(0|[1-9][0-9]*)'
LOWERCASE_PRERELEASE='[0-9a-z-]+(\.[0-9a-z-]+)*'
BUILD_METADATA='[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*'
NORMALIZED_VERSION="^$NUMERIC_COMPONENT\.$NUMERIC_COMPONENT\.$NUMERIC_COMPONENT(-$LOWERCASE_PRERELEASE)?(\+$BUILD_METADATA)?$"

normalized_candidate() {
  local version="$1" core prerelease metadata part candidate
  core="${version%%[-+]*}"
  prerelease="${version#"$core"}"
  prerelease="${prerelease%%+*}"
  metadata="${version#"$core$prerelease"}"
  prerelease="$(printf '%s' "$prerelease" | tr '[:upper:]' '[:lower:]')"
  [[ "$core" =~ ^[0-9]+(\.[0-9]+){1,3}$ ]] || return 1
  local -a parts normalized_parts=()
  IFS=. read -r -a parts <<< "$core"
  for part in "${parts[@]}"; do
    normalized_parts+=("$((10#$part))")
  done
  while [ "${#normalized_parts[@]}" -lt 3 ]; do
    normalized_parts+=(0)
  done
  if [ "${#normalized_parts[@]}" -eq 4 ] && [ "${normalized_parts[3]}" -eq 0 ]; then
    normalized_parts=("${normalized_parts[@]:0:3}")
  fi
  candidate="$(IFS=.; echo "${normalized_parts[*]}")$prerelease$metadata"
  [[ "$candidate" =~ $NORMALIZED_VERSION ]] || return 1
  echo "$candidate"
}

if ! [[ "$EFFECTIVE_RUNTIME" =~ $NORMALIZED_VERSION ]]; then
  if candidate="$(normalized_candidate "$EFFECTIVE_RUNTIME")"; then
    echo "::error::runtime_version='$EFFECTIVE_RUNTIME' is not a NuGet-normalized version; use '$candidate'"
  else
    echo "::error::runtime_version='$EFFECTIVE_RUNTIME' is not a NuGet-normalized version; expected MAJOR.MINOR.PATCH[-prerelease][+metadata] without leading zeroes or an uppercase prerelease"
  fi
  exit 1
fi
if ! [[ "$EFFECTIVE_CODEGEN_CS" =~ ^[A-Za-z0-9.+-]+$ ]]; then
  echo "::error::codegen_cs_version='$EFFECTIVE_CODEGEN_CS' must be a valid version string"; exit 1
fi
if ! [[ "$EFFECTIVE_DAML_SDK" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]]; then
  echo "::error::daml_sdk_version='$EFFECTIVE_DAML_SDK' must match ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$"; exit 1
fi
if ! [[ "$EFFECTIVE_PACKAGE_LICENSE" =~ ^[A-Za-z0-9.+()-]+([[:blank:]]+(AND|OR|WITH)[[:blank:]]+[A-Za-z0-9.+()-]+)*$ ]]; then
  echo "::error::package_license='$EFFECTIVE_PACKAGE_LICENSE' must be a valid SPDX expression"; exit 1
fi

if [ -n "${GITHUB_ENV:-}" ]; then
  {
    echo "RUNTIME_VERSION=$EFFECTIVE_RUNTIME"
    echo "CODEGEN_CS_VERSION=$EFFECTIVE_CODEGEN_CS"
    echo "DAML_SDK_VERSION=$EFFECTIVE_DAML_SDK"
    echo "PACKAGE_LICENSE=$EFFECTIVE_PACKAGE_LICENSE"
    echo "DRY_RUN=$EFFECTIVE_DRY_RUN"
  } >> "$GITHUB_ENV"
fi
echo "Resolved [${SOURCE_LABEL:-}]: runtime=$EFFECTIVE_RUNTIME codegen-cs=$EFFECTIVE_CODEGEN_CS daml-sdk=$EFFECTIVE_DAML_SDK license=$EFFECTIVE_PACKAGE_LICENSE dry_run=$EFFECTIVE_DRY_RUN"
