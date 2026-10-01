#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

EXIT_BROKEN=2
DEFAULT_REPO=peacefulstudio/canton-dotnet-sdk
WORKFLOW_FILE=cross-version-matrix.yaml
RESULTS_ARTIFACT=cross-version-matrix

usage() {
  cat <<EOF
Usage: $(basename "${BASH_SOURCE[0]}") render <results-dir>
       $(basename "${BASH_SOURCE[0]}") fetch [--repo <owner/name>] [--run <run-id>]

Render the Canton cross-version compatibility matrix as Markdown.

  render   read every <results-dir>/<cell>/cell.json and the MTP test logs
           (<cell>/*.log) beside it, and print a cell table followed by a
           per-suite table.
  fetch    download the '${RESULTS_ARTIFACT}' artifact of a
           ${WORKFLOW_FILE} run on <owner/name> (default ${DEFAULT_REPO}) and
           render it, followed by a link to that run. Without --run it takes
           the latest completed run on main. Paste the output into the
           release notes.

cell.json carries: order, cell, localnet_ref, image_tag, expected_canton,
reported_canton, gating (true/false) and outcome (the cell job's status).

Exit codes: 0 rendered, ${EXIT_BROKEN} the table could not be produced.
EOF
}

broken() {
  echo "cross-version-matrix-table: $*" >&2
  exit "${EXIT_BROKEN}"
}

suite_counts() {
  awk '
    BEGIN { ansi = sprintf("%c", 27) "\\[[0-9;]*[A-Za-z]" }
    { gsub(ansi, ""); gsub(/\r/, "") }
    /^[[:space:]]*total:[[:space:]]*[0-9]+[[:space:]]*$/     { summaries++; total += $2 }
    /^[[:space:]]*failed:[[:space:]]*[0-9]+[[:space:]]*$/    { failed += $2 }
    /^[[:space:]]*succeeded:[[:space:]]*[0-9]+[[:space:]]*$/ { succeeded += $2 }
    /^[[:space:]]*skipped:[[:space:]]*[0-9]+[[:space:]]*$/   { skipped += $2 }
    END { print summaries + 0, total + 0, succeeded + 0, failed + 0, skipped + 0 }
  ' "$1"
}

outcome_badge() {
  case "$1" in
    success) echo "pass" ;;
    failure) echo "FAIL" ;;
    cancelled) echo "cancelled" ;;
    *) echo "$1" ;;
  esac
}

cell_files_in_order() {
  local results_dir="$1" cell_file order
  while IFS= read -r -d '' cell_file; do
    order="$(jq -er '.order | numbers' "${cell_file}")" || broken "${cell_file} has no numeric 'order'"
    printf '%s\t%s\n' "${order}" "${cell_file}"
  done < <(find "${results_dir}" -mindepth 2 -maxdepth 2 -name cell.json -print0) |
    sort -n -k1,1 |
    cut -f2-
}

render() {
  local results_dir="$1"
  [ -d "${results_dir}" ] || broken "results directory not found: ${results_dir}"

  local cell_files
  cell_files="$(cell_files_in_order "${results_dir}")"
  [ -n "${cell_files}" ] || broken "no <cell>/cell.json under ${results_dir}"

  echo "| Cell | LocalNet | Splice images | Expected Canton | Participant reports | Gating | Result |"
  echo "| --- | --- | --- | --- | --- | --- | --- |"
  while IFS= read -r cell_file; do
    jq -r --arg result "$(outcome_badge "$(jq -r '.outcome' "${cell_file}")")" '
      "| \(.cell) | `\(.localnet_ref)` | \(if .image_tag == "" then "release default" else "`\(.image_tag)`" end) | "
      + "\(if .expected_canton == "" then "any 3.5" else "`\(.expected_canton)`" end) | "
      + "\(if .reported_canton == "" then "UNAVAILABLE" else "`\(.reported_canton)`" end) | "
      + "\(if .gating then "yes" else "no (canary)" end) | \($result) |"
    ' "${cell_file}"
  done <<<"${cell_files}"

  echo ""
  echo "| Cell | Suite | Succeeded | Failed | Skipped | Total |"
  echo "| --- | --- | --- | --- | --- | --- |"
  while IFS= read -r cell_file; do
    local cell_dir cell_name log summaries total succeeded failed skipped found=false
    cell_dir="$(dirname "${cell_file}")"
    cell_name="$(jq -r '.cell' "${cell_file}")"
    for log in "${cell_dir}"/*.log; do
      [ -f "${log}" ] || continue
      found=true
      read -r summaries total succeeded failed skipped <<<"$(suite_counts "${log}")"
      if [ "${summaries}" -eq 0 ]; then
        echo "| ${cell_name} | $(basename "${log}" .log) | - | - | - | no test summary |"
      else
        echo "| ${cell_name} | $(basename "${log}" .log) | ${succeeded} | ${failed} | ${skipped} | ${total} |"
      fi
    done
    if [ "${found}" = false ]; then
      echo "| ${cell_name} | - | - | - | - | no suite ran |"
    fi
  done <<<"${cell_files}"
}

fetch() {
  local repo="${DEFAULT_REPO}" run_id=""
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --repo) repo="${2:?--repo needs a value}"; shift 2 ;;
      --run) run_id="${2:?--run needs a value}"; shift 2 ;;
      *) usage >&2; broken "unknown fetch argument: $1" ;;
    esac
  done

  if [ -z "${run_id}" ]; then
    run_id="$(gh run list --repo "${repo}" --workflow "${WORKFLOW_FILE}" --branch main --status completed \
      --limit 1 --json databaseId --jq '.[0].databaseId // empty')"
    [ -n "${run_id}" ] || broken "no completed ${WORKFLOW_FILE} run on ${repo} main"
  fi

  fetch_download_dir="$(mktemp -d)"
  trap 'rm -rf "${fetch_download_dir}"' EXIT
  gh run download "${run_id}" --repo "${repo}" --name "${RESULTS_ARTIFACT}" --dir "${fetch_download_dir}" ||
    broken "could not download the ${RESULTS_ARTIFACT} artifact of run ${run_id} on ${repo}"

  render "${fetch_download_dir}"
  echo ""
  echo "Source: https://github.com/${repo}/actions/runs/${run_id}"
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
  render)
    [ "$#" -eq 2 ] || { usage >&2; broken "render takes exactly one <results-dir>"; }
    render "$2"
    ;;
  fetch)
    shift
    fetch "$@"
    ;;
  *)
    usage >&2
    broken "expected 'render' or 'fetch', got '${1:-}'"
    ;;
esac
