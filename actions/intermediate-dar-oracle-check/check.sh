#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

: "${RELEASE_BASE_URL:=https://github.com/peacefulstudio/canton-dotnet-sdk/releases/download}"
: "${SPLICE_GIT_URL:=https://github.com/canton-network/splice}"
: "${SPLICE_RAW_URL:=https://raw.githubusercontent.com/canton-network/splice}"
: "${GITHUB_OUTPUT:=/dev/null}"
: "${GITHUB_STEP_SUMMARY:=/dev/null}"

INTERMEDIATE_DAR_TYPE="daml.codegen.intermediate.IntermediateDar"

fail() {
  echo "::error::$1" >&2
  exit 1
}

require_tool() {
  command -v "$1" > /dev/null || fail "$1 is required on PATH"
}

validate_inputs() {
  [ -n "${INPUT_PRODUCER:-}" ] || fail "input 'producer' is required"
  case "${INPUT_TIER:-}" in
    full | shape) ;;
    *) fail "input 'tier' must be 'full' or 'shape', got '${INPUT_TIER:-}'" ;;
  esac
  if [ -n "${INPUT_ORACLE_JAR:-}" ]; then
    [ -f "$INPUT_ORACLE_JAR" ] || fail "input 'oracle-jar' does not name a file: ${INPUT_ORACLE_JAR}"
  else
    [[ "${INPUT_VERSION:-}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-[0-9A-Za-z.-]+)?$ ]] \
      || fail "input 'version' must be a release version such as 0.8.0 unless 'oracle-jar' is set, got '${INPUT_VERSION:-}'"
  fi
  case "${INPUT_SPLICE:-}" in
    "" | latest) ;;
    *)
      [[ "$INPUT_SPLICE" =~ ^([0-9]+\.[0-9]+\.[0-9]+|[0-9a-f]{40})$ ]] \
        || fail "input 'splice' must be empty, 'latest', a version such as 0.9.1, or a 40-hex commit, got '${INPUT_SPLICE}'"
      ;;
  esac
}

fetch_release_asset() {
  local name=$1
  curl -fsSL --retry 3 "${RELEASE_BASE_URL}/v${INPUT_VERSION}/${name}" -o "${ASSETS}/${name}" \
    || fail "release v${INPUT_VERSION} has no asset ${name}"
  [ "$name" = SHA256SUMS ] && return
  (cd "$ASSETS" && grep -F "  ${name}" SHA256SUMS | sha256sum -c - > /dev/null) \
    || fail "${name} does not match the release's SHA256SUMS"
}

verify_release_matches_checkout() {
  fetch_release_asset "intermediate_dar-${INPUT_VERSION}.proto"
  cmp -s "${ASSETS}/intermediate_dar-${INPUT_VERSION}.proto" "${REPO_ROOT}/proto/intermediate_dar.proto" \
    || fail "the Action's checkout and release v${INPUT_VERSION} disagree on intermediate_dar.proto: use the Action at the tag v${INPUT_VERSION}"
}

collect_corpus_dars() {
  local dar
  while IFS= read -r dar; do
    add_dar "$(basename "$dar" .dar)" "$dar" corpus
  done < <(find "${REPO_ROOT}/conformance" -name '*.dar' | LC_ALL=C sort)
  [ "${#DAR_NAMES[@]}" -gt 0 ] || fail "no corpus DAR under ${REPO_ROOT}/conformance"
}

add_dar() {
  DAR_NAMES+=("$1")
  DAR_PATHS+=("$2")
  DAR_SOURCES+=("$3")
}

manifest_tier_entry() {
  local name=$1
  jq -c --arg name "$name" --arg tier "$INPUT_TIER" \
    '.fixtures[] | select(.name == $name) | .[$tier] // empty' "${FIXTURES}/manifest.json"
}

verify_fixture_tarball() {
  fetch_release_asset "intermediate-fixtures-${INPUT_VERSION}.tar.gz"
  mkdir -p "$FIXTURES"
  tar -xzf "${ASSETS}/intermediate-fixtures-${INPUT_VERSION}.tar.gz" -C "$FIXTURES"
  [ -f "${FIXTURES}/manifest.json" ] \
    || fail "release v${INPUT_VERSION} ships no fixtures manifest.json: it predates the tiered fixtures"
  local i name dar_sha manifest_dar_sha tier_entry file expected_sha actual_sha key
  for i in "${!DAR_NAMES[@]}"; do
    [ "${DAR_SOURCES[$i]}" = corpus ] || continue
    name=${DAR_NAMES[$i]}
    tier_entry=$(manifest_tier_entry "$name")
    [ -n "$tier_entry" ] || fail "${name} (tier ${INPUT_TIER}): manifest.json has no entry for it"
    dar_sha=$(sha256sum "${DAR_PATHS[$i]}" | awk '{print $1}')
    manifest_dar_sha=$(jq -r --arg name "$name" '.fixtures[] | select(.name == $name) | .source.darSha256' "${FIXTURES}/manifest.json")
    [ "$dar_sha" = "$manifest_dar_sha" ] || fail "${name}.dar at v${INPUT_VERSION} does not match the manifest's darSha256"
    for key in binpb json; do
      file=$(jq -r --arg key "$key" '.[$key]' <<< "$tier_entry")
      expected_sha=$(jq -r --arg key "${key}Sha256" '.[$key]' <<< "$tier_entry")
      [ -f "${FIXTURES}/${file}" ] \
        || fail "${name} (tier ${INPUT_TIER}): manifest.json lists ${file} but the tarball does not contain it"
      actual_sha=$(sha256sum "${FIXTURES}/${file}" | awk '{print $1}')
      [ "$actual_sha" = "$expected_sha" ] || fail "${name} (tier ${INPUT_TIER}): ${file} does not match the manifest's ${key}Sha256"
    done
  done
}

resolve_splice_commit() {
  local spec=$1 tag
  if [[ "$spec" =~ ^[0-9a-f]{40}$ ]]; then
    echo "$spec"
    return
  fi
  if [ "$spec" = latest ]; then
    tag=$(git ls-remote --tags --refs "$SPLICE_GIT_URL" \
      | sed -E 's|.*refs/tags/||' | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | LC_ALL=C sort -V | tail -n 1)
    [ -n "$tag" ] || fail "no release tag found in ${SPLICE_GIT_URL}"
  else
    tag=$spec
  fi
  local commit
  commit=$(git ls-remote "$SPLICE_GIT_URL" "refs/tags/${tag}" "refs/tags/${tag}^{}" | tail -n 1 | awk '{print $1}')
  [ -n "$commit" ] || fail "splice has no tag ${tag}"
  echo "$commit"
}

latest_dar_per_splice_family() {
  local names families family
  names=$(grep -E '^splice-.*\.dar$' | grep -v -- '-current' || true)
  families=$(sed -E 's/\.dar$//; s/-[0-9].*$//' <<< "$names" | sort -u)
  while IFS= read -r family; do
    [ -n "$family" ] || continue
    grep -E "^${family}-[0-9][^/]*\.dar$" <<< "$names" | LC_ALL=C sort -V | tail -n 1
  done <<< "$families"
}

fetch_splice_dars() {
  SPLICE_COMMIT=$(resolve_splice_commit "$INPUT_SPLICE")
  echo "splice-commit: ${SPLICE_COMMIT}"
  local tree="${WORK}/splice-tree" dars="${WORK}/splice-dars" file
  git init --quiet "$tree"
  git -C "$tree" fetch --quiet --depth 1 --filter=blob:none "$SPLICE_GIT_URL" "$SPLICE_COMMIT" \
    || fail "cannot fetch ${SPLICE_COMMIT} from ${SPLICE_GIT_URL}"
  mkdir -p "$dars"
  local selected
  selected=$(git -C "$tree" ls-tree --name-only FETCH_HEAD daml/dars/ | sed 's|^daml/dars/||' | latest_dar_per_splice_family)
  [ -n "$selected" ] || fail "splice commit ${SPLICE_COMMIT} has no splice-* DAR under daml/dars/"
  while IFS= read -r file; do
    curl -fsSL --retry 3 "${SPLICE_RAW_URL}/${SPLICE_COMMIT}/daml/dars/${file}" -o "${dars}/${file}" \
      || fail "cannot fetch ${file} at splice ${SPLICE_COMMIT}"
    add_dar "$(basename "$file" .dar)" "${dars}/${file}" splice
  done <<< "$selected"
  echo "splice: $(wc -l <<< "$selected" | tr -d ' ') DARs at ${SPLICE_COMMIT}"
}

canonical_json() {
  local binpb=$1 json=$2 raw="${2}.raw"
  (cd "$PROTO_DIR" && buf convert intermediate_dar.proto \
    --type "$INTERMEDIATE_DAR_TYPE" \
    --from "${binpb}#format=binpb" \
    --to "${raw}#format=json") || return 1
  jq -S . "$raw" > "$json"
  rm -f "$raw"
}

oracle_json() {
  local dar=$1 json=$2 flags=()
  [ "$INPUT_TIER" = shape ] && flags=(--schema-only)
  java -jar "$ORACLE_JAR" "${flags[@]}" --dar "$dar" --out "${json}.binpb" \
    || fail "the oracle jar failed on ${dar}"
  canonical_json "${json}.binpb" "$json" || fail "cannot convert the oracle's output for ${dar}"
}

run_producer() {
  local dar=$1 out=$2
  (
    export DAR="$dar" OUT="$out" TIER="$INPUT_TIER"
    bash -eo pipefail -c "$INPUT_PRODUCER"
  )
}

check_dar() {
  local name=$1 dar=$2 source=$3
  local expected="${WORK}/expected/${name}.json" actual_binpb="${WORK}/actual/${name}.binpb" actual="${WORK}/actual/${name}.json"
  if [ "$source" = corpus ] && [ -z "${INPUT_ORACLE_JAR:-}" ]; then
    cp "${FIXTURES}/$(manifest_tier_entry "$name" | jq -r .json)" "$expected"
  else
    oracle_json "$dar" "$expected"
  fi

  if ! run_producer "$dar" "$actual_binpb"; then
    record "$name" FAIL "producer exited non-zero"
    return
  fi
  if [ ! -s "$actual_binpb" ]; then
    record "$name" FAIL "producer wrote no output to \$OUT"
    return
  fi
  if ! canonical_json "$actual_binpb" "$actual"; then
    record "$name" FAIL "producer output is not a valid IntermediateDar"
    return
  fi
  if diff -u --label "expected/${name}.json" --label "actual/${name}.json" "$expected" "$actual" > "${WORK}/diff.txt"; then
    record "$name" PASS ""
  else
    echo "::group::JSON diff for ${name} (tier ${INPUT_TIER})"
    cat "${WORK}/diff.txt"
    echo "::endgroup::"
    record "$name" FAIL "canonical JSON differs"
  fi
}

record() {
  local name=$1 result=$2 detail=$3
  if [ "$result" = FAIL ]; then
    MISMATCHES=$((MISMATCHES + 1))
    echo "::error::${name} (tier ${INPUT_TIER}): ${detail}"
  else
    echo "PASS ${name} (tier ${INPUT_TIER})"
  fi
  SUMMARY_ROWS+=("| ${name} | ${INPUT_TIER} | ${result} |")
}

write_report() {
  {
    echo "### Intermediate DAR oracle check"
    echo
    echo "| DAR | Tier | Result |"
    echo "|---|---|---|"
    printf '%s\n' "${SUMMARY_ROWS[@]}"
    if [ -n "$SPLICE_COMMIT" ]; then
      echo
      echo "Splice commit: \`${SPLICE_COMMIT}\`"
    fi
  } >> "$GITHUB_STEP_SUMMARY"
  {
    echo "mismatches=${MISMATCHES}"
    echo "splice-commit=${SPLICE_COMMIT}"
  } >> "$GITHUB_OUTPUT"
}

main() {
  validate_inputs
  require_tool buf
  require_tool jq
  require_tool git
  require_tool curl

  WORK=$(mktemp -d)
  ASSETS="${WORK}/assets"
  FIXTURES="${WORK}/fixtures"
  PROTO_DIR="${WORK}/proto"
  trap 'rm -rf "$WORK"' EXIT
  mkdir -p "$ASSETS" "$PROTO_DIR" "${WORK}/expected" "${WORK}/actual"
  DAR_NAMES=() DAR_PATHS=() DAR_SOURCES=() SUMMARY_ROWS=()
  MISMATCHES=0
  SPLICE_COMMIT=""
  ORACLE_JAR=${INPUT_ORACLE_JAR:-}
  REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)

  cp "${REPO_ROOT}/proto/intermediate_dar.proto" "${PROTO_DIR}/intermediate_dar.proto"
  collect_corpus_dars
  if [ -z "$ORACLE_JAR" ]; then
    fetch_release_asset SHA256SUMS
    verify_release_matches_checkout
    verify_fixture_tarball
    if [ -n "${INPUT_SPLICE:-}" ]; then
      fetch_release_asset "daml-dar-to-proto-${INPUT_VERSION}.jar"
      ORACLE_JAR="${ASSETS}/daml-dar-to-proto-${INPUT_VERSION}.jar"
    fi
  fi
  if [ -n "${INPUT_SPLICE:-}" ]; then
    fetch_splice_dars
  fi
  if [ -n "$ORACLE_JAR" ]; then
    require_tool java
  fi

  local i
  for i in "${!DAR_NAMES[@]}"; do
    check_dar "${DAR_NAMES[$i]}" "${DAR_PATHS[$i]}" "${DAR_SOURCES[$i]}"
  done

  write_report
  echo "${MISMATCHES} mismatch(es) over ${#DAR_NAMES[@]} DAR(s)"
  [ "$MISMATCHES" -eq 0 ]
}

main
