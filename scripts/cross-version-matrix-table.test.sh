#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

usage() {
  cat <<EOF
Usage: $0

Regression test for scripts/cross-version-matrix-table.sh. Builds a results
directory holding two cells, then asserts:

  1. render prints the cell table in 'order', not directory order, with the
     release-default image, the canary and the unavailable version spelled out;
  2. render prints one per-suite row per MTP log, summing ANSI-coloured
     multi-assembly summaries, and flags a log with no summary and a cell
     with no log;
  3. render fails with exit 2 on an empty results directory and on a
     cell.json without a numeric order;
  4. fetch resolves the latest completed main run through a stub gh, renders
     the downloaded artifact and links the run;
  5. fetch --run skips the lookup and downloads that run.

Exits non-zero on any assertion failure.
EOF
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
renderer="${script_dir}/cross-version-matrix-table.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "${work_dir}"' EXIT

failures=0

pass() { echo "  ok: $1"; }
fail() {
  echo "  FAIL: $1" >&2
  failures=$((failures + 1))
}

expect_output() {
  local description="$1" expected="$2"
  shift 2
  local actual
  actual="$("$@" 2>&1)"
  if [ "${actual}" = "${expected}" ]; then
    pass "${description}"
  else
    fail "${description}"
    diff <(printf '%s\n' "${expected}") <(printf '%s\n' "${actual}") >&2
  fi
}

expect_status() {
  local description="$1" expected="$2"
  shift 2
  local status=0
  "$@" >/dev/null 2>&1 || status=$?
  if [ "${status}" -eq "${expected}" ]; then
    pass "${description} (exit ${status})"
  else
    fail "${description}: expected exit ${expected}, got ${status}"
  fi
}

write_summary() {
  local path="$1" total="$2" succeeded="$3" skipped="$4" esc
  esc="$(printf '\033')"
  cat >>"${path}" <<LOG
${esc}[32mTest run summary: Passed!
${esc}[m  total: ${total}
  failed: 0
${esc}[32m  succeeded: ${succeeded}
${esc}[m  skipped: ${skipped}
  duration: 6s 744ms
LOG
}

results="${work_dir}/results"
mkdir -p "${results}/a-canary" "${results}/z-baseline"

cat >"${results}/z-baseline/cell.json" <<'JSON'
{"order":1,"cell":"Canton 3.5.18","localnet_ref":"v0.8.3-2","image_tag":"","expected_canton":"3.5.18","reported_canton":"3.5.18","gating":true,"outcome":"success"}
JSON
cat >"${results}/a-canary/cell.json" <<'JSON'
{"order":2,"cell":"LocalNet dev","localnet_ref":"dev","image_tag":"0.8.1","expected_canton":"","reported_canton":"","gating":false,"outcome":"failure"}
JSON
write_summary "${results}/z-baseline/grpc-integration.log" 12 10 2
write_summary "${results}/z-baseline/grpc-integration.log" 8 8 0
printf 'Build succeeded.\n' >"${results}/z-baseline/rest-conformance.log"

echo "=== render ==="

expect_output "render prints the cell table in order, then one row per suite" \
  "$(cat <<'EOF'
| Cell | LocalNet | Splice images | Expected Canton | Participant reports | Gating | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Canton 3.5.18 | `v0.8.3-2` | release default | `3.5.18` | `3.5.18` | yes | pass |
| LocalNet dev | `dev` | `0.8.1` | any 3.5 | UNAVAILABLE | no (canary) | FAIL |

| Cell | Suite | Succeeded | Failed | Skipped | Total |
| --- | --- | --- | --- | --- | --- |
| Canton 3.5.18 | grpc-integration | 18 | 0 | 2 | 20 |
| Canton 3.5.18 | rest-conformance | - | - | - | no test summary |
| LocalNet dev | - | - | - | - | no suite ran |
EOF
)" \
  "${renderer}" render "${results}"

mkdir -p "${work_dir}/empty"
expect_status "render rejects a results directory with no cell" 2 "${renderer}" render "${work_dir}/empty"

mkdir -p "${work_dir}/unordered/cell"
printf '{"cell":"x"}\n' >"${work_dir}/unordered/cell/cell.json"
expect_status "render rejects a cell.json without a numeric order" 2 "${renderer}" render "${work_dir}/unordered"

echo "=== fetch ==="

stub_bin="${work_dir}/bin"
mkdir -p "${stub_bin}"
cat >"${stub_bin}/gh" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"${GH_STUB_LOG}"
case "$1 $2" in
  "run list") echo "4242" ;;
  "run download")
    while [ "$#" -gt 0 ]; do
      [ "$1" = "--dir" ] && cp -R "${GH_STUB_RESULTS}/." "$2"
      shift
    done
    ;;
  *) exit 1 ;;
esac
STUB
chmod +x "${stub_bin}/gh"

mkdir -p "${work_dir}/single/baseline"
cp "${results}/z-baseline/cell.json" "${work_dir}/single/baseline/cell.json"

export GH_STUB_LOG="${work_dir}/gh.log"
export GH_STUB_RESULTS="${work_dir}/single"

fetch_expected="$(cat <<'EOF'
| Cell | LocalNet | Splice images | Expected Canton | Participant reports | Gating | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Canton 3.5.18 | `v0.8.3-2` | release default | `3.5.18` | `3.5.18` | yes | pass |

| Cell | Suite | Succeeded | Failed | Skipped | Total |
| --- | --- | --- | --- | --- | --- |
| Canton 3.5.18 | - | - | - | - | no suite ran |

Source: https://github.com/peacefulstudio/canton-dotnet-sdk/actions/runs/4242
EOF
)"

: >"${GH_STUB_LOG}"
expect_output "fetch renders the latest completed main run and links it" "${fetch_expected}" \
  env PATH="${stub_bin}:${PATH}" "${renderer}" fetch
if grep -q '^run list --repo peacefulstudio/canton-dotnet-sdk --workflow cross-version-matrix.yaml --branch main --status completed' "${GH_STUB_LOG}" &&
  grep -q '^run download 4242 --repo peacefulstudio/canton-dotnet-sdk --name cross-version-matrix --dir ' "${GH_STUB_LOG}"; then
  pass "fetch looks up the latest completed main run, then downloads its cross-version-matrix artifact"
else
  fail "fetch did not call gh as expected: $(cat "${GH_STUB_LOG}")"
fi

: >"${GH_STUB_LOG}"
expect_output "fetch --run renders that run" "${fetch_expected//4242/77}" \
  env PATH="${stub_bin}:${PATH}" "${renderer}" fetch --run 77
if grep -q '^run list' "${GH_STUB_LOG}"; then
  fail "fetch --run still looked up the latest run"
else
  pass "fetch --run skips the latest-run lookup"
fi

if [ "${failures}" -ne 0 ]; then
  echo "${failures} assertion(s) failed" >&2
  exit 1
fi
echo "all cross-version-matrix-table assertions passed"
