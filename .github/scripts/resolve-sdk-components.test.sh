#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/resolve-sdk-components.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
export SDK_MANIFEST_DIR="${work}"

failures=0

write_manifest() {
  local sdk="$1"
  shift
  {
    echo "apiVersion: digitalasset.com/v1"
    echo "kind: SdkManifest"
    echo "spec:"
    echo "  components:"
    for entry in "$@"; do
      echo "    ${entry%%=*}:"
      echo "      version: ${entry#*=}"
    done
    echo "  assistant:"
    echo "    version: 1.0.22"
    echo "  version: ${sdk}"
    echo "  edition: open-source"
  } > "${work}/${sdk}.yaml"
}

expect() {
  local name="$1" want_status="$2" want_text="$3" output status
  output="$("${script}" "${4}" 2>&1)"
  status=$?
  if [ "${status}" -ne "${want_status}" ] || [[ "${output}" != *"${want_text}"* ]]; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

write_manifest 3.5.12 canton-open-source=3.5.19 daml-script=3.5.3 damlc=3.5.3 scribe=3.5.8
expect "SDK 3.5.12 ships damlc 3.5.3" 0 "damlc=3.5.3 daml-script=3.5.3" 3.5.12

write_manifest 3.6.0 codegen=3.6.2 daml-script=3.6.2 damlc=3.6.2
expect "SDK 3.6.0 ships damlc 3.6.2" 0 "damlc=3.6.2 daml-script=3.6.2" 3.6.0

write_manifest 3.7.0 damlc=3.7.1 daml-script=3.7.4
expect "damlc and daml-script versions are read independently" 0 "damlc=3.7.1 daml-script=3.7.4" 3.7.0

write_manifest 3.8.0 daml-script=3.8.0
expect "a manifest without damlc fails loudly" 1 "lists no 'damlc' component" 3.8.0

write_manifest 3.9.0 damlc=3.9.0
expect "a manifest without daml-script fails loudly" 1 "lists no 'daml-script' component" 3.9.0

expect "a malformed SDK version is refused" 1 "must match" "3.5"

exit $((failures > 0 ? 1 : 0))
