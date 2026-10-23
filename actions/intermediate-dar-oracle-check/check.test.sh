#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

usage() {
  cat <<EOF
Usage: $0

Regression test for check.sh in release-tarball mode (no oracle-jar). Builds throwaway
releases served over file:// and asserts the Action:

  1. passes when the manifest lists each fixture at the published layout, <tier>/<name>.json;
  2. compares against the JSON the manifest names, not a hardcoded <tier>/<name>.json, so a
     manifest that relocates its fixtures still passes;
  3. never reads an unlisted <tier>/<name>.json lying next to a relocated, listed fixture;
  4. fails naming the DAR and the tier when the manifest has no entry for a DAR;
  5. fails naming the DAR, the tier and the file when the manifest lists a file the tarball lacks.

Needs buf, jq and a sha256sum on PATH.
EOF
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
checker="${script_dir}/check.sh"
repo_root="$(cd "${script_dir}/../.." && pwd)"
version=9.9.9
dar_type=daml.codegen.intermediate.IntermediateDar

tmp_root="$(mktemp -d)"
trap 'rm -rf "${tmp_root}"' EXIT

failures=0

corpus_dar_names() {
  find "${repo_root}/conformance" -name '*.dar' | LC_ALL=C sort | while IFS= read -r dar; do
    basename "$dar" .dar
  done
}

corpus_dar_path() {
  find "${repo_root}/conformance" -name "$1.dar" | head -n 1
}

sha_of() {
  sha256sum "$1" | awk '{print $1}'
}

convert() {
  (cd "${repo_root}/proto" && buf convert intermediate_dar.proto --type "$dar_type" --from "$1" --to "$2")
}

stub_binpb="${tmp_root}/stub.binpb"
stub_json="${tmp_root}/stub.json"
decoy_json="${tmp_root}/decoy.json"
printf '{"main":{"packageName":"Stub"}}' >"${tmp_root}/stub.raw.json"
printf '{"main":{"packageName":"Decoy"}}' >"${tmp_root}/decoy.raw.json"
convert "${tmp_root}/stub.raw.json#format=json" "${stub_binpb}#format=binpb"
convert "${stub_binpb}#format=binpb" "${tmp_root}/stub.canon.json#format=json"
jq -S . "${tmp_root}/stub.canon.json" >"${stub_json}"
convert "${tmp_root}/decoy.raw.json#format=json" "${tmp_root}/decoy.binpb#format=binpb"
convert "${tmp_root}/decoy.binpb#format=binpb" "${tmp_root}/decoy.canon.json#format=json"
jq -S . "${tmp_root}/decoy.canon.json" >"${decoy_json}"

place_fixture() {
  local root=$1 directory=$2 stem=$3
  mkdir -p "${root}/${directory}"
  cp "${stub_binpb}" "${root}/${directory}/${stem}.binpb"
  cp "${stub_json}" "${root}/${directory}/${stem}.json"
}

tier_entry() {
  local root=$1 directory=$2 stem=$3
  jq -n \
    --arg binpb "${directory}/${stem}.binpb" \
    --arg binpbSha256 "$(sha_of "${root}/${directory}/${stem}.binpb")" \
    --arg json "${directory}/${stem}.json" \
    --arg jsonSha256 "$(sha_of "${root}/${directory}/${stem}.json")" \
    '{binpb: $binpb, binpbSha256: $binpbSha256, json: $json, jsonSha256: $jsonSha256}'
}

build_release() {
  local scenario=$1 serving="${tmp_root}/${1}-serving" content="${tmp_root}/${1}-content" entries="${tmp_root}/${1}-entries.jsonl"
  local name full_dir=full shape_dir=shape stem full_entry
  mkdir -p "${serving}/v${version}" "${content}"
  : >"$entries"
  [ "$scenario" = published ] || { full_dir=layout-full; shape_dir=layout-shape; }
  while IFS= read -r name; do
    stem=$name
    [ "$scenario" = published ] || stem="${name}.expected"
    place_fixture "$content" "$full_dir" "$stem"
    place_fixture "$content" "$shape_dir" "$stem"
    if [ "$scenario" = decoy ]; then
      mkdir -p "${content}/full"
      cp "$decoy_json" "${content}/full/${name}.json"
    fi
    [ "$scenario" = missing-entry ] && [ "$name" = richtypes ] && continue
    full_entry=$(tier_entry "$content" "$full_dir" "$stem")
    if [ "$scenario" = missing-file ] && [ "$name" = richtypes ]; then
      rm "${content}/${full_dir}/${stem}.json"
    fi
    jq -n \
      --arg name "$name" \
      --arg darSha256 "$(sha_of "$(corpus_dar_path "$name")")" \
      --argjson full "$full_entry" \
      --argjson shape "$(tier_entry "$content" "$shape_dir" "$stem")" \
      '{name: $name, source: {darSha256: $darSha256}, full: $full, shape: $shape}' >>"$entries"
  done < <(corpus_dar_names)
  jq -n --slurpfile fixtures "$entries" '{version: "9.9.9", fixtures: $fixtures}' >"${content}/manifest.json"
  tar -czf "${serving}/v${version}/intermediate-fixtures-${version}.tar.gz" -C "$content" .
  cp "${repo_root}/proto/intermediate_dar.proto" "${serving}/v${version}/intermediate_dar-${version}.proto"
  (cd "${serving}/v${version}" && sha256sum "intermediate-fixtures-${version}.tar.gz" "intermediate_dar-${version}.proto" >SHA256SUMS)
  printf '%s' "$serving"
}

run_check() {
  local serving=$1 output_file=$2
  RELEASE_BASE_URL="file://${serving}" \
    INPUT_VERSION="$version" \
    INPUT_TIER=full \
    INPUT_PRODUCER='cp "$STUB_BINPB" "$OUT"' \
    STUB_BINPB="$stub_binpb" \
    GITHUB_OUTPUT="${output_file}.out" \
    GITHUB_STEP_SUMMARY="${output_file}.summary" \
    "$checker" >"$output_file" 2>&1
}

expect() {
  local expected="$1" description="$2" scenario="$3" needle="${4:-}" output status=0 serving
  serving=$(build_release "$scenario")
  run_check "$serving" "${tmp_root}/${scenario}.log" || status=$?
  output=$(cat "${tmp_root}/${scenario}.log")
  if [ "$status" -ne "$expected" ]; then
    echo "FAIL: ${description}: expected exit ${expected}, got ${status}: ${output}"
    failures=$((failures + 1))
    return
  fi
  if [ -n "$needle" ] && [[ "$output" != *"$needle"* ]]; then
    echo "FAIL: ${description}: output lacks '${needle}': ${output}"
    failures=$((failures + 1))
    return
  fi
  echo "ok: ${description}"
}

expect 0 "published layout passes" published
expect 0 "a relocated manifest layout passes" relocated
expect 0 "an unlisted fixture beside a relocated one is ignored" decoy
expect 1 "a DAR missing from the manifest is named with its tier" missing-entry "richtypes (tier full)"
expect 1 "a listed file missing from the tarball is named with its DAR and tier" missing-file "richtypes (tier full)"

if [ "$failures" -ne 0 ]; then
  echo "${failures} failure(s)"
  exit 1
fi
