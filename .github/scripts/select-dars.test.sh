#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/select-dars.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

mkdir -p "${work}/bundle/node/dars"
touch "${work}/bundle/node/dars/splice-amulet-1.0.0.dar" "${work}/bundle/node/dars/other.dar"
tar -czf "${work}/bundle.tar.gz" -C "${work}/bundle" node
bundle_sha256="$(sha256sum "${work}/bundle.tar.gz" | cut -d' ' -f1)"
wrong_sha256="$(printf '%064d' 0)"

failures=0

run_select() {
  local sha256="$1"
  rm -rf "${work}/run" && mkdir -p "${work}/run"
  output="$(cd "${work}/run" \
    && DOWNLOAD_URL="file://${work}/bundle.tar.gz" \
      DOWNLOAD_SHA256="${sha256}" \
      DAR_FIND_PATH='*/dars/*.dar' \
      DAR_PATTERNS='splice-*' \
      SOURCE_LABEL=Test \
      "${script}" 2>&1)"
  status=$?
}

expect() {
  local name="$1" want_status="$2" want_text="${3:-}"
  if { [ "${want_status}" = "0" ] && [ "${status}" -ne 0 ]; } \
    || { [ "${want_status}" != "0" ] && [ "${status}" -eq 0 ]; } \
    || { [ -n "${want_text}" ] && [[ "${output}" != *"${want_text}"* ]]; }; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

run_select "${bundle_sha256}"
expect "a matching digest extracts and selects the DAR" 0 "Selected 1 DAR(s)"
[ -f "${work}/run/selected-dars/splice-amulet-1.0.0.dar" ] || { echo "FAIL matching digest left no selected DAR"; failures=$((failures + 1)); }

run_select "${wrong_sha256}"
expect "a wrong digest fails and names the mismatch" 1 "SHA-256 mismatch"
[ ! -d "${work}/run/extracted" ] || [ -z "$(ls -A "${work}/run/extracted")" ] || { echo "FAIL a wrong digest still extracted the bundle"; failures=$((failures + 1)); }

run_select ""
expect "a missing digest fails closed" 1 "DOWNLOAD_SHA256"

run_select "$(printf '%s' "${bundle_sha256}" | tr a-f A-F)"
expect "an uppercase digest still matches" 0 "Selected 1 DAR(s)"

if [ "${failures}" -ne 0 ]; then
  echo "${failures} assertion(s) failed"
  exit 1
fi
echo "all select-dars assertions passed"
