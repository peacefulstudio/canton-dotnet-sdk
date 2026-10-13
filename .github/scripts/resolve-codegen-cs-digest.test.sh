#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/resolve-codegen-cs-digest.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

built_digest="sha256:1111111111111111111111111111111111111111111111111111111111111111"
published_digest="sha256:2222222222222222222222222222222222222222222222222222222222222222"

mkdir -p "${work}/bin"
cat > "${work}/bin/oras" <<'ORAS'
#!/usr/bin/env bash
if [ "$1" = "resolve" ] && [ "$2" = "ghcr.io/peacefulstudio/dpm-codegen-cs:0.6.0" ]; then
  echo "sha256:2222222222222222222222222222222222222222222222222222222222222222"
else
  echo "Error: $2: not found" >&2
  exit 1
fi
ORAS
chmod +x "${work}/bin/oras"

failures=0

expect() {
  local name="$1" want_status="$2" want_text="$3"
  shift 3
  : > "${work}/github-output"
  local output status
  output="$(PATH="${work}/bin:${PATH}" GITHUB_OUTPUT="${work}/github-output" "${script}" "$@" 2>&1)"
  status=$?
  if [ "${status}" -ne "${want_status}" ] || [[ "${output}" != *"${want_text}"* ]]; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

expect_output() {
  local name="$1" want_line="$2"
  if grep -qxF "${want_line}" "${work}/github-output"; then
    echo "ok   ${name}"
  else
    echo "FAIL ${name}: GITHUB_OUTPUT lacks '${want_line}': $(tr '\n' ' ' < "${work}/github-output")"
    failures=$((failures + 1))
  fi
}

expect "a release uses the digest the build pushed" 0 "pinned to the digest oci-build pushed" 0.7.0-preview.1 "${built_digest}" false
expect_output "the built digest is exported" "digest=sha256:1111111111111111111111111111111111111111111111111111111111111111"

expect "a release with no built digest fails closed" 1 "oci-build pushed no index digest" 0.7.0-preview.1 "" false
expect "a release with a malformed built digest fails closed" 1 "oci-build pushed no index digest" 0.7.0-preview.1 "latest" false

expect "a dry run resolves the already-published tag" 0 "not pinned to a build output" 0.6.0 "" true
expect_output "the published digest is exported" "digest=sha256:2222222222222222222222222222222222222222222222222222222222222222"

expect "a dry run never uses a built digest" 0 "not pinned to a build output" 0.6.0 "${built_digest}" true
expect_output "the published digest wins on a dry run" "digest=${published_digest}"

expect "a dry run on an unpublished tag fails closed" 1 "ghcr.io/peacefulstudio/dpm-codegen-cs:0.9.9 does not resolve" 0.9.9 "" true

expect "a missing argument is refused" 1 "Usage" 0.7.0-preview.1

exit $((failures > 0 ? 1 : 0))
