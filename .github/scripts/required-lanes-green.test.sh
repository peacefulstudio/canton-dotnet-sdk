#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/required-lanes-green.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
mkdir -p "${work}/bin" "${work}/runs"

cat > "${work}/bin/gh" <<'STUB'
#!/usr/bin/env bash
workflow=""
filter="."
while [ $# -gt 0 ]; do
  [ "$1" = "--workflow" ] && workflow="$2"
  [ "$1" = "--jq" ] && filter="$2"
  shift
done
jq "${filter}" "${RUNS_DIR}/${workflow}.json"
STUB
chmod +x "${work}/bin/gh"

export RUNS_DIR="${work}/runs"
export PATH="${work}/bin:${PATH}"

failures=0

set_runs() {
  printf '%s' "$2" > "${RUNS_DIR}/$1.json"
}

run_gate() {
  output="$("${script}" abc123 ci.yaml localnet.yaml multisync.yaml 2>&1)"
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

green='[{"status":"completed","conclusion":"success"}]'
red='[{"status":"completed","conclusion":"failure"}]'
pending='[{"status":"in_progress","conclusion":""}]'
none='[]'

reset_all_green() {
  set_runs ci.yaml "${green}"
  set_runs localnet.yaml "${green}"
  set_runs multisync.yaml "${green}"
}

reset_all_green
run_gate
expect "all green passes" 0

reset_all_green
set_runs localnet.yaml "${pending}"
run_gate
expect "one pending fails and names it" 1 "localnet.yaml"

reset_all_green
set_runs ci.yaml "${red}"
run_gate
expect "one red fails and names it" 1 "ci.yaml"

reset_all_green
set_runs multisync.yaml "${none}"
run_gate
expect "one missing fails and names it" 1 "multisync.yaml"

reset_all_green
set_runs localnet.yaml '[{"status":"completed","conclusion":"success"},{"status":"completed","conclusion":"failure"}]'
run_gate
expect "a green rerun counts as green" 0

reset_all_green
set_runs ci.yaml "${red}"
set_runs multisync.yaml "${pending}"
run_gate
expect "every waited-on lane is named" 1 "multisync.yaml"
[[ "${output}" == *"ci.yaml"* ]] || { echo "FAIL both lanes named: ${output}"; failures=$((failures + 1)); }

[ "${failures}" -eq 0 ]
