#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

mkdir -p "${work}/repo/.github/scripts"
cp "${here}/resolve-inputs.sh" "${work}/repo/.github/scripts/resolve-inputs.sh"
script="${work}/repo/.github/scripts/resolve-inputs.sh"

failures=0

props_version() {
  printf '<Project>\n  <PropertyGroup>\n    <Version>%s</Version>\n  </PropertyGroup>\n</Project>\n' "$1" \
    > "${work}/repo/Directory.Build.props"
}

expect_runtime() {
  local name="$1" runtime_input="$2" want_status="$3" want_text="$4"
  : > "${work}/github-env"
  local output status
  output="$(RUNTIME_VERSION_INPUT="${runtime_input}" \
    CODEGEN_CS_VERSION_INPUT=0.7.1 \
    DAML_SDK_VERSION_INPUT=3.4.11 \
    GITHUB_ENV="${work}/github-env" \
    "${script}" 2>&1)"
  status=$?
  if [ "${status}" -ne "${want_status}" ] || [[ "${output}" != *"${want_text}"* ]]; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

props_version 0.7.1
expect_runtime "a stable runtime version passes" 0.7.1 0 "runtime=0.7.1"
expect_runtime "a preview runtime version passes" 0.8.0-preview.4 0 "runtime=0.8.0-preview.4"
expect_runtime "a runtime version with build metadata passes" 0.8.0-preview.4+abc123 0 "runtime=0.8.0-preview.4+abc123"
expect_runtime "multi-digit components pass" 10.20.300 0 "runtime=10.20.300"
expect_runtime "an empty input defaults to the Directory.Build.props version" "" 0 "runtime=0.7.1"

props_version 0.6.0-preview.4
expect_runtime "a preview Directory.Build.props version passes" "" 0 "runtime=0.6.0-preview.4"

props_version 0.7.1
expect_runtime "a zero fourth component is rejected with its normalized form" 0.7.1.0 1 "::error::runtime_version='0.7.1.0' is not a NuGet-normalized version; use '0.7.1'"
expect_runtime "a leading zero is rejected with its normalized form" 0.07.1 1 "runtime_version='0.07.1' is not a NuGet-normalized version; use '0.7.1'"
expect_runtime "a leading zero keeps the prerelease and metadata in the normalized form" 01.7.1-preview.4+abc 1 "use '1.7.1-preview.4+abc'"
expect_runtime "a two-part version is rejected with its normalized form" 0.7 1 "runtime_version='0.7' is not a NuGet-normalized version; use '0.7.0'"
expect_runtime "a non-zero fourth component is rejected with the expected shape" 0.7.1.5 1 "runtime_version='0.7.1.5' is not a NuGet-normalized version; expected MAJOR.MINOR.PATCH[-prerelease][+metadata] without leading zeroes"
expect_runtime "an uppercase prerelease label is rejected with its lowercase form" 0.8.0-Preview.4 1 "::error::runtime_version='0.8.0-Preview.4' is not a NuGet-normalized version; use '0.8.0-preview.4'"
expect_runtime "an uppercase prerelease label keeps the build metadata as given" 0.8.0-RC.1+AbC 1 "use '0.8.0-rc.1+AbC'"
expect_runtime "uppercase build metadata alone is accepted" 0.8.0-preview.4+AbC 0 "runtime=0.8.0-preview.4+AbC"
expect_runtime "a non-version string is rejected" "latest" 1 "runtime_version='latest' is not a NuGet-normalized version"
expect_runtime "a rejected runtime version writes nothing to GITHUB_ENV" 0.7.1.0 1 ""
if [ -s "${work}/github-env" ]; then
  echo "FAIL a rejected runtime version still wrote GITHUB_ENV: $(cat "${work}/github-env")"
  failures=$((failures + 1))
fi

if [ "${failures}" -ne 0 ]; then
  echo "${failures} assertion(s) failed"
  exit 1
fi
echo "all assertions passed"
