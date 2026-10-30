#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="${here}/seed-local-feed.sh"
generator="${here}/../../src/Daml.Codegen.CSharp/CodeGen/ProjectFileGenerator.cs"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

mkdir -p "${work}/bin"
cat > "${work}/bin/gh" <<'STUB'
#!/usr/bin/env bash
echo "gh $*" >> "${STUB_LOG}"
[ "${GH_STUB_MODE}" = "fail" ] && { echo "release not found" >&2; exit 1; }
dir=""
while [ $# -gt 0 ]; do
  [ "$1" = "--dir" ] && dir="$2"
  shift
done
for asset in ${GH_STUB_ASSETS:-}; do touch "${dir}/${asset}"; done
STUB
cat > "${work}/bin/curl" <<'STUB'
#!/usr/bin/env bash
echo "curl $*" >> "${STUB_LOG}"
url="${*: -1}"
index="${CURL_STUB_DIR}/$(basename "$(dirname "${url}")").json"
[ -f "${index}" ] || exit 22
cat "${index}"
STUB
chmod +x "${work}/bin/gh" "${work}/bin/curl"

failures=0

index_with_versions() {
  local id="$1"; shift
  local versions
  versions="$(printf '"%s",' "$@")"
  printf '{"versions":[%s]}' "${versions%,}" > "${work}/indexes/${id}.json"
}

run_seed() {
  local gh_mode="$1" assets="$2" version="${3-0.7.1}"
  rm -rf "${work}/runner" "${work}/run.log" && mkdir -p "${work}/runner"
  : > "${work}/stub.log"
  output="$(PATH="${work}/bin:${PATH}" \
    STUB_LOG="${work}/stub.log" \
    CURL_STUB_DIR="${work}/indexes" \
    GH_STUB_MODE="${gh_mode}" \
    GH_STUB_ASSETS="${assets}" \
    GITHUB_REPOSITORY=example/repo \
    RELEASE_TAG=v0.7.1 \
    RUNNER_TEMP="${work}/runner" \
    RUNTIME_VERSION="${version}" \
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

expect_log() {
  local name="$1" want_text="$2"
  if grep -qF -- "${want_text}" "${work}/stub.log"; then
    echo "ok   ${name}"
  else
    echo "FAIL ${name}: stub log lacks '${want_text}': $(cat "${work}/stub.log")"
    failures=$((failures + 1))
  fi
}

expect_no_log() {
  local name="$1" unwanted_text="$2"
  if grep -qF -- "${unwanted_text}" "${work}/stub.log"; then
    echo "FAIL ${name}: stub log has '${unwanted_text}'"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

reset_indexes() {
  rm -rf "${work}/indexes" && mkdir -p "${work}/indexes"
}

both_assets="Daml.Runtime.0.7.1.nupkg Daml.Ledger.Abstractions.0.7.1.nupkg"

reset_indexes
run_seed ok "${both_assets}"
expect "download ok with both core packages seeds the feed and passes" 0 "Daml.Ledger.Abstractions.0.7.1.nupkg"
expect_no_log "nuget.org is not consulted when the local feed has both" "curl"

reset_indexes
run_seed fail ""
expect "download fail and not indexed fails with an error annotation" 1 "::error::"
expect "the error names the missing package and version" 1 "Daml.Runtime 0.7.1"
expect "the error names the release tag" 1 "v0.7.1"
expect_log "the flat-container index is looked up by lowercase id" "/v3-flatcontainer/daml.runtime/index.json"

reset_indexes
index_with_versions daml.runtime 0.7.0 0.7.1
index_with_versions daml.ledger.abstractions 0.7.0 0.7.1
run_seed fail ""
expect "download fail but both already indexed keeps passing" 0

reset_indexes
index_with_versions daml.runtime 0.7.0 0.7.1
index_with_versions daml.ledger.abstractions 0.7.0
run_seed fail ""
expect "download fail with one package indexed at another version fails" 1 "Daml.Ledger.Abstractions 0.7.1"

reset_indexes
index_with_versions daml.ledger.abstractions 0.7.1
run_seed ok "Daml.Runtime.0.7.1.nupkg"
expect "download ok with one package local and the other indexed passes" 0

reset_indexes
run_seed ok "Daml.Runtime.0.7.1.nupkg"
expect "download ok but one core package neither local nor indexed fails" 1 "Daml.Ledger.Abstractions 0.7.1"

reset_indexes
index_with_versions daml.runtime 0.8.0-preview.4
index_with_versions daml.ledger.abstractions 0.8.0-preview.4
run_seed fail "" "0.8.0-Preview.4+abc123"
expect "a mixed-case prerelease with build metadata matches its normalised index entry" 0

reset_indexes
run_seed ok "Daml.Runtime.0.8.0-preview.4.nupkg Daml.Ledger.Abstractions.0.8.0-preview.4.nupkg" "0.8.0-preview.4+abc123"
expect "a runtime version with build metadata matches the metadata-free local nupkg names" 0 "in the local feed"
expect_no_log "nuget.org is not consulted when the metadata-free local nupkgs are present" "curl"

reset_indexes
run_seed ok "${both_assets}" ""
expect "an empty runtime version fails closed" 1 "RUNTIME_VERSION"

for id in Daml.Runtime Daml.Ledger.Abstractions; do
  if grep -qF "<PackageReference Include=\\\"${id}\\\"" "${generator}"; then
    echo "ok   the generator still references ${id}"
  else
    echo "FAIL the generator no longer references ${id}"
    failures=$((failures + 1))
  fi
done

if [ "${failures}" -ne 0 ]; then
  echo "${failures} assertion(s) failed"
  exit 1
fi
echo "all assertions passed"
