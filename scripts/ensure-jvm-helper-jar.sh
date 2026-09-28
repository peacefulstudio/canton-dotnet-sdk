#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 [--check-only]

Ensures jvm-helper/target/scala-2.13/daml-dar-to-proto.jar was assembled
from the working tree's current helper sources, then prints its path on stdout.

The JAR is gitignored build output, so a working tree can hold a JAR assembled
from sources that no longer exist. Checked-in codegen hashes regenerated
against such a JAR pass locally and then fail CI, which always assembles
fresh; the tell is a pinned intermediate-proto SHA moving on a C#-only diff.

This script closes that gap. It hashes every input the assembly consumes — the
build definition, the main sources, and the repo-root proto/ tree that build.sbt
compiles through sbt-protoc — and reassembles whenever that hash differs from
the one stamped beside the JAR by the last successful assembly. An up-to-date
JAR costs a few file hashes and never starts sbt.

Assembly output is written to stderr so stdout carries only the JAR path.

Options:
  --check-only   Fail instead of assembling when the JAR is missing or stale.
EOF
  exit "${1:-1}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

CHECK_ONLY=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --check-only) CHECK_ONLY="1"; shift 1 ;;
    -h|--help) usage 0 ;;
    *) echo "ensure-jvm-helper-jar.sh: unknown arg: $1" >&2; usage ;;
  esac
done

HELPER_DIR="$PROJECT_ROOT/jvm-helper"
HELPER_JAR="$HELPER_DIR/target/scala-2.13/daml-dar-to-proto.jar"
SOURCE_STAMP="$HELPER_JAR.sources.sha256"

ASSEMBLY_INPUTS=(
  jvm-helper/build.sbt
  jvm-helper/project
  jvm-helper/src/main
  proto
)

sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum
  else
    shasum -a 256
  fi
}

assembly_input_hash() {
  cd "$PROJECT_ROOT"
  {
    find "${ASSEMBLY_INPUTS[@]}" -type f -print \
      | LC_ALL=C sort \
      | while IFS= read -r path; do
          printf '%s  %s\n' "$(sha256_stream < "$path" | awk '{print $1}')" "$path"
        done
    printf 'env:DAML_DAR_TO_PROTO_VERSION=%s\n' "${DAML_DAR_TO_PROTO_VERSION:-}"
  } | sha256_stream | awk '{print $1}'
}

for input in "${ASSEMBLY_INPUTS[@]}"; do
  [[ -e "$PROJECT_ROOT/$input" ]] || {
    echo "ensure-jvm-helper-jar.sh: JVM helper assembly input is missing from the working tree: $input" >&2
    echo "The JAR cannot be proven current against a tree this incomplete; refusing to guess." >&2
    exit 1
  }
done

EXPECTED_SOURCES="$(assembly_input_hash)"

if [[ -f "$HELPER_JAR" && -f "$SOURCE_STAMP" && "$(<"$SOURCE_STAMP")" == "$EXPECTED_SOURCES" ]]; then
  echo "$HELPER_JAR"
  exit 0
fi

if [[ -f "$HELPER_JAR" ]]; then
  STAMPED="$([[ -f "$SOURCE_STAMP" ]] && echo "$(<"$SOURCE_STAMP")" || echo "none (assembled outside this script)")"
  if [[ -f "$SOURCE_STAMP" ]]; then
    STATE="stale — it was assembled from helper sources that no longer match the working tree"
  else
    STATE="freshness unverifiable — jar was assembled outside this script (no stamp)"
  fi
else
  STATE="missing"
  STAMPED="none"
fi

if [[ -n "$CHECK_ONLY" ]]; then
  echo "ensure-jvm-helper-jar.sh: JVM helper JAR is $STATE" >&2
  echo "  jar:              $HELPER_JAR" >&2
  echo "  sources expected: $EXPECTED_SOURCES" >&2
  echo "  sources stamped:  $STAMPED" >&2
  echo "Assemble it with: scripts/ensure-jvm-helper-jar.sh" >&2
  exit 1
fi

command -v sbt >/dev/null 2>&1 || {
  echo "ensure-jvm-helper-jar.sh: JVM helper JAR is $STATE, and 'sbt' is not on PATH to assemble it." >&2
  echo "  jar: $HELPER_JAR" >&2
  echo "Install sbt (https://www.scala-sbt.org), then re-run: scripts/ensure-jvm-helper-jar.sh" >&2
  exit 1
}

echo "ensure-jvm-helper-jar.sh: JVM helper JAR is $STATE — assembling from $HELPER_DIR" >&2
rm -f "$SOURCE_STAMP"
(cd "$HELPER_DIR" && sbt assembly) >&2

[[ -f "$HELPER_JAR" ]] || {
  echo "ensure-jvm-helper-jar.sh: 'sbt assembly' reported success but produced no JAR at $HELPER_JAR" >&2
  exit 1
}

printf '%s\n' "$EXPECTED_SOURCES" > "$SOURCE_STAMP"
echo "$HELPER_JAR"
