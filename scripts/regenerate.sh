#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

ENTRIES="crossmodule-dar snapshots quickstart conformance determinism"

usage() {
  cat <<EOF
Usage: $0 write         [--only <entry>]... [--package <name>]... [--snapshot <name>]... [--keep-protos] [tool options]
       $0 check         [--only <entry>]... [--package <name>]... [--snapshot <name>]... [--keep-protos] [tool options]
       $0 rebuild-dar   --package <dir-under-conformance> --base <base-name> [tool options]

The one place that knows which checked-in artefacts derive from a DAR, which
tools turn the DAR into each of them, and the single pinned emitter invocation
every one of them goes through.

  write        Regenerates every artefact in place. Run it after an emitter,
               type-mapper, JVM-helper or corpus-DAR change and commit the diff.
  check        Regenerates every artefact into a scratch directory and compares it
               byte for byte with the checked-in copy, then runs the determinism
               pipeline a second time and compares the two runs. Exits
               non-zero on any difference. This is what CI runs.
  rebuild-dar  Rebuilds a conformance fixture's committed DAR after a change to its
               Daml sources: re-derives the content-addressed package name
               (scripts/set-conformance-package-name.sh), runs 'dpm build', replaces
               the committed DAR, then regenerates its bindings with 'write --only
               conformance', and the determinism pins too when the package is the
               determinism fixture.
               Commit the changed daml.yaml, DAR and generated tree together.

Every tool the selected entries run is built or validated first: the JVM helper
JAR is reassembled from source when stale (scripts/ensure-jvm-helper-jar.sh) and
the C# emitter CLI is rebuilt in Release, so a stale binary can never stage stale
output.

Entries (default: all):
  crossmodule-dar  tests/Daml.Codegen.CSharp.Tests/Snapshots/cross-module-collision/
                   cross-module-collision.dar, a byte copy of
                   conformance/crossmodulecollision/crossmodulecollision.dar
  snapshots        every Snapshots/<name>/intermediate.binpb (JVM helper, from the
                   DAR vendored beside it) and Snapshots/<name>/expected/ (emitter)
  quickstart       QuickstartSample/intermediate.binpb (JVM helper, from the vendored
                   quickstart DAR) and samples/QuickstartExample/Generated (emitter)
  conformance      src/Daml.Codegen.Testing.Conformance/Generated, one folder per
                   Daml module of every shipped fixture: ${SHIPPED_PACKAGES[*]}
  determinism      tests/determinism/shas/ pins of the richtypes pipeline
Entries in this checkout: $ENTRIES

Options:
  --only <entry>        Restrict to this entry. Repeatable.
  --package <name>      Restrict the conformance entry to this package (and select
                        it). Repeatable; replaces only the module folders that
                        package emits.
  --snapshot <name>     Restrict the snapshots entry to this snapshot (and select
                        it). Repeatable. Selecting the cross-module-collision
                        snapshot selects crossmodule-dar too, so the snapshot never
                        decodes a stale copy of the conformance DAR.
  --keep-protos         Treat the vendored intermediate.binpb files as inputs: do
                        not decode their DARs, regenerate only what the emitter
                        derives from them (no JDK needed for snapshots/quickstart).
  --helper-jar <path>   daml-dar-to-proto.jar. Overriding skips the freshness
                        check; you own the JAR's currency.
  --java-bin <path>     Java executable that runs the JAR. (default: java on PATH)
  --cli-bin <path>      Daml.Codegen.CSharp.Cli binary. Overriding skips the rebuild;
                        you own the binary's currency.
                        (default: built from src/Daml.Codegen.CSharp.Cli, Release)
  --base <base-name>    rebuild-dar only: the package-name base.
EOF
  exit "${1:-1}"
}

die() {
  echo "regenerate.sh: $*" >&2
  exit 1
}

SHIPPED_PACKAGES=(richtypes contractkeys defaulttarget)

SNAPSHOTS_REL="tests/Daml.Codegen.CSharp.Tests/Snapshots"
CROSSMODULE_SOURCE_DAR_REL="conformance/crossmodulecollision/crossmodulecollision.dar"
CROSSMODULE_SNAPSHOT_FAMILY="cross-module-collision"
CROSSMODULE_SNAPSHOT_DAR_REL="$SNAPSHOTS_REL/$CROSSMODULE_SNAPSHOT_FAMILY/$CROSSMODULE_SNAPSHOT_FAMILY.dar"
QUICKSTART_DAR_REL="tests/Daml.Codegen.CSharp.Tests/QuickstartSample/quickstart-0.0.1.dar"
QUICKSTART_PROTO_REL="tests/Daml.Codegen.CSharp.Tests/QuickstartSample/intermediate.binpb"
QUICKSTART_GENERATED_REL="samples/QuickstartExample/Generated"
CONFORMANCE_GENERATED_REL="src/Daml.Codegen.Testing.Conformance/Generated"
CONFORMANCE_ROOT_NAMESPACE="Daml.Codegen.Testing.Conformance"
CONFORMANCE_ROOT_NAMESPACE_PATH="${CONFORMANCE_ROOT_NAMESPACE//./\/}"
DETERMINISM_DAR_REL="conformance/richtypes/richtypes.dar"
DETERMINISM_SHAS_REL="tests/determinism/shas"
EMITTER_PROJECT_REL="src/Daml.Codegen.CSharp.Cli"

PINNED_EMITTER_ARGS=(
  --target-framework net10.0
  --root ".*"
  --nullable true
  --include-dependencies false
  --package-license Apache-2.0
  --emitter-counter 0
  --verbosity 1
)

UNPROMOTED_ENTRIES_FILE="$SCRIPT_DIR/regenerate-unpromoted-entries.sh"
if [[ -f "$UNPROMOTED_ENTRIES_FILE" ]]; then
  source "$UNPROMOTED_ENTRIES_FILE"
fi

[[ $# -ge 1 ]] || usage
MODE="$1"
shift 1
case "$MODE" in
  write|check|rebuild-dar) ;;
  -h|--help) usage 0 ;;
  *) echo "regenerate.sh: unknown mode: $MODE" >&2; usage ;;
esac

ONLY=()
PACKAGES=()
SNAPSHOTS=()
BASE=""
KEEP_PROTOS=false
HELPER_JAR=""
JAVA_BIN="java"
CLI_BIN=""
TOOL_ARGS=()

require_value() {
  [[ $# -ge 2 && "$2" != -* ]] || { echo "regenerate.sh: $1 requires a value" >&2; usage; }
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --only) require_value "$@"; ONLY+=("$2"); shift 2 ;;
    --package) require_value "$@"; PACKAGES+=("$2"); shift 2 ;;
    --snapshot) require_value "$@"; SNAPSHOTS+=("$2"); shift 2 ;;
    --base) require_value "$@"; BASE="$2"; shift 2 ;;
    --keep-protos) KEEP_PROTOS=true; shift 1 ;;
    --helper-jar) require_value "$@"; HELPER_JAR="$2"; TOOL_ARGS+=("$1" "$2"); shift 2 ;;
    --java-bin) require_value "$@"; JAVA_BIN="$2"; TOOL_ARGS+=("$1" "$2"); shift 2 ;;
    --cli-bin) require_value "$@"; CLI_BIN="$2"; TOOL_ARGS+=("$1" "$2"); shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "regenerate.sh: unknown arg: $1" >&2; usage ;;
  esac
done

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

function_name_of() {
  printf '%s_%s' "$1" "${2//-/_}"
}

rebuild_dar() {
  [[ ${#PACKAGES[@]} -eq 1 && -n "$BASE" ]] || {
    echo "regenerate.sh: rebuild-dar takes exactly one --package and a --base" >&2
    usage
  }
  local package="${PACKAGES[0]}" package_dir package_name built_dars write_args
  package_dir="$PROJECT_ROOT/conformance/$package"

  [[ -d "$package_dir" ]] || die "no such package directory: $package_dir"
  command -v dpm >/dev/null 2>&1 || die "'dpm' not found on PATH"

  package_name="$("$SCRIPT_DIR/set-conformance-package-name.sh" --package "$package" --base "$BASE")"

  (cd "$package_dir" && dpm build)

  built_dars=("$package_dir"/.daml/dist/"$package_name"-*.dar)
  [[ ${#built_dars[@]} -eq 1 && -f "${built_dars[0]}" ]] || {
    die "expected exactly one DAR matching $package_dir/.daml/dist/$package_name-*.dar, found: ${built_dars[*]}"
  }
  cp "${built_dars[0]}" "$package_dir/$package.dar"

  write_args=(write --only conformance --package "$package")
  if [[ "conformance/$package/$package.dar" == "$DETERMINISM_DAR_REL" ]]; then
    write_args+=(--only determinism)
  fi
  "$SCRIPT_DIR/regenerate.sh" "${write_args[@]}" ${TOOL_ARGS[@]+"${TOOL_ARGS[@]}"}

  echo ""
  echo "Rebuilt $package_name."
}

if [[ "$MODE" == "rebuild-dar" ]]; then
  rebuild_dar
  exit 0
fi

[[ -z "$BASE" ]] || die "--base belongs to rebuild-dar"

entry_is_known() {
  local candidate
  for candidate in $ENTRIES; do
    [[ "$candidate" == "$1" ]] && return 0
  done
  return 1
}

snapshot_is_selected() {
  local family
  [[ ${#SNAPSHOTS[@]} -gt 0 ]] || return 0
  for family in "${SNAPSHOTS[@]}"; do
    [[ "$family" == "$1" ]] && return 0
  done
  return 1
}

select_entries() {
  local requested="" entry
  if [[ ${#ONLY[@]} -gt 0 ]]; then
    for entry in "${ONLY[@]}"; do
      entry_is_known "$entry" || die "unknown entry: $entry (known: $ENTRIES)"
      requested="$requested $entry"
    done
  fi
  [[ ${#PACKAGES[@]} -eq 0 ]] || requested="$requested conformance"
  [[ ${#SNAPSHOTS[@]} -eq 0 ]] || requested="$requested snapshots"
  if [[ " $requested " == *" snapshots "* ]] && snapshot_is_selected "$CROSSMODULE_SNAPSHOT_FAMILY"; then
    requested="$requested crossmodule-dar"
  fi

  SELECTED=""
  for entry in $ENTRIES; do
    if [[ -z "$requested" || " $requested " == *" $entry "* ]]; then
      SELECTED="$SELECTED $entry"
    fi
  done
}

select_entries

PARTIAL_CONFORMANCE=true
if [[ ${#PACKAGES[@]} -gt 0 ]]; then
  for package in "${PACKAGES[@]}"; do
    [[ "$package" =~ ^[A-Za-z0-9_-]+$ ]] || die "invalid package name: $package"
    [[ -f "$PROJECT_ROOT/conformance/$package/$package.dar" ]] || die "fixture DAR not found: conformance/$package/$package.dar"
  done
else
  PACKAGES=("${SHIPPED_PACKAGES[@]}")
  PARTIAL_CONFORMANCE=false
fi

list_snapshot_families() {
  (cd "$PROJECT_ROOT/$SNAPSHOTS_REL" && find . -mindepth 1 -maxdepth 1 -type d | sed 's|^\./||' | LC_ALL=C sort)
}

resolve_snapshot_families() {
  FAMILIES=()
  local family
  if [[ ${#SNAPSHOTS[@]} -gt 0 ]]; then
    for family in "${SNAPSHOTS[@]}"; do
      if [[ "$family" =~ [/\\] ]] || [[ "$family" == *".."* ]]; then
        die "snapshot name must not contain path separators or '..': $family"
      fi
      [[ -d "$PROJECT_ROOT/$SNAPSHOTS_REL/$family" ]] || die "snapshot not found: $SNAPSHOTS_REL/$family"
      FAMILIES+=("$family")
    done
    return
  fi
  while IFS= read -r family; do
    FAMILIES+=("$family")
  done < <(list_snapshot_families)
  [[ ${#FAMILIES[@]} -gt 0 ]] || die "no snapshot families found under $SNAPSHOTS_REL"
}

tools_crossmodule_dar() { :; }

tools_snapshots() {
  echo emitter
  [[ "$KEEP_PROTOS" == true ]] || echo helper
}

tools_quickstart() { tools_snapshots; }

tools_conformance() { printf 'emitter\nhelper\n'; }

tools_determinism() { printf 'emitter\nhelper\n'; }

prepare_helper() {
  command -v "$JAVA_BIN" >/dev/null 2>&1 || die "'$JAVA_BIN' not found"
  if [[ -z "$HELPER_JAR" ]]; then
    HELPER_JAR="$("$SCRIPT_DIR/ensure-jvm-helper-jar.sh")"
    return
  fi
  [[ -f "$HELPER_JAR" ]] || die "JVM helper JAR not found: $HELPER_JAR (build it with: scripts/ensure-jvm-helper-jar.sh)"
}

prepare_emitter() {
  if [[ -z "$CLI_BIN" ]]; then
    dotnet build "$PROJECT_ROOT/$EMITTER_PROJECT_REL" -c Release --nologo -v quiet -clp:NoSummary
    CLI_BIN="$PROJECT_ROOT/$EMITTER_PROJECT_REL/bin/Release/net10.0/Daml.Codegen.CSharp.Cli"
  fi
  [[ -x "$CLI_BIN" ]] || die "emitter CLI not found or not executable: $CLI_BIN"
}

prepare_tools() {
  local entry tool needed=""
  for entry in $SELECTED; do
    needed="$needed $("$(function_name_of tools "$entry")")"
  done
  for tool in $(printf '%s\n' $needed | LC_ALL=C sort -u); do
    "$(function_name_of prepare "$tool")"
  done
}

input_path() {
  if [[ -e "$STAGE/out/$1" ]]; then
    printf '%s\n' "$STAGE/out/$1"
  else
    printf '%s\n' "$PROJECT_ROOT/$1"
  fi
}

claim() {
  local entry="$1" rel="$2" path="$STAGE/out/$2"
  if [[ -d "$path" ]]; then
    [[ -n "$(find "$path" -type f | head -n 1)" ]] || die "$entry: the pipeline produced no file under $rel; refusing to treat an empty tree as the artefact"
  else
    [[ -f "$path" ]] || die "$entry: the pipeline did not produce $rel"
  fi
  printf '%s\t%s\n' "$entry" "$rel" >> "$STAGE/claims"
}

decode_with_helper() {
  local dar_rel="$1" dar
  PROTO="$STAGE/protos/$dar_rel.binpb"
  [[ ! -f "$PROTO" ]] || return 0
  dar="$(input_path "$dar_rel")"
  [[ -f "$dar" ]] || die "fixture DAR not found: $dar_rel (a snapshot vendors the DAR its intermediate.binpb is generated from; vendor it, or pass --keep-protos to keep the vendored proto)"
  mkdir -p "$(dirname "$PROTO")"
  "$JAVA_BIN" -jar "$HELPER_JAR" --dar "$dar" --out "$PROTO" >/dev/null || die "the JVM helper failed on $dar_rel"
}

emit() {
  local proto="$1" out_dir="$2"
  shift 2
  mkdir -p "$out_dir"
  "$CLI_BIN" --intermediate "$proto" -o "$out_dir" "${PINNED_EMITTER_ARGS[@]}" "$@" >/dev/null || die "the emitter failed on $proto"
}

produce_crossmodule_dar() {
  local source
  source="$PROJECT_ROOT/$CROSSMODULE_SOURCE_DAR_REL"
  [[ -f "$source" ]] || die "fixture DAR not found: $CROSSMODULE_SOURCE_DAR_REL"
  mkdir -p "$(dirname "$STAGE/out/$CROSSMODULE_SNAPSHOT_DAR_REL")"
  cp "$source" "$STAGE/out/$CROSSMODULE_SNAPSHOT_DAR_REL"
  claim crossmodule-dar "$CROSSMODULE_SNAPSHOT_DAR_REL"
}

vendored_or_decoded_proto() {
  local dar_rel="$1" proto_rel="$2"
  if [[ "$KEEP_PROTOS" == true ]]; then
    [[ -f "$PROJECT_ROOT/$proto_rel" ]] || die "vendored proto not found: $proto_rel"
    PROTO="$PROJECT_ROOT/$proto_rel"
    return 0
  fi
  decode_with_helper "$dar_rel"
}

stage_proto() {
  local entry="$1" proto_rel="$2"
  if [[ "$KEEP_PROTOS" == true ]]; then
    return 0
  fi
  mkdir -p "$(dirname "$STAGE/out/$proto_rel")"
  cp "$PROTO" "$STAGE/out/$proto_rel"
  claim "$entry" "$proto_rel"
}

produce_snapshots() {
  local family family_rel expected_rel
  resolve_snapshot_families
  for family in "${FAMILIES[@]}"; do
    family_rel="$SNAPSHOTS_REL/$family"
    expected_rel="$family_rel/expected"
    vendored_or_decoded_proto "$family_rel/$family.dar" "$family_rel/intermediate.binpb"
    stage_proto snapshots "$family_rel/intermediate.binpb"
    emit "$PROTO" "$STAGE/out/$expected_rel"
    if [[ -f "$PROJECT_ROOT/$expected_rel/.gitkeep" && -z "$(find "$STAGE/out/$expected_rel" -type f | head -n 1)" ]]; then
      cp "$PROJECT_ROOT/$expected_rel/.gitkeep" "$STAGE/out/$expected_rel/.gitkeep"
    fi
    claim snapshots "$expected_rel"
  done
}

produce_quickstart() {
  vendored_or_decoded_proto "$QUICKSTART_DAR_REL" "$QUICKSTART_PROTO_REL"
  stage_proto quickstart "$QUICKSTART_PROTO_REL"
  emit "$PROTO" "$STAGE/out/$QUICKSTART_GENERATED_REL"
  claim quickstart "$QUICKSTART_GENERATED_REL"
}

produce_conformance() {
  local package raw_dir emitted_root module_dir module owner leftover
  mkdir -p "$STAGE/module-owners" "$STAGE/out/$CONFORMANCE_GENERATED_REL"
  for package in "${PACKAGES[@]}"; do
    decode_with_helper "conformance/$package/$package.dar"
    raw_dir="$STAGE/conformance-raw/$package"
    emit "$PROTO" "$raw_dir" --namespace "$CONFORMANCE_ROOT_NAMESPACE"

    emitted_root="$raw_dir/$CONFORMANCE_ROOT_NAMESPACE_PATH"
    [[ -d "$emitted_root" ]] || {
      echo "regenerate.sh: the emitter produced nothing under $CONFORMANCE_ROOT_NAMESPACE for $package." >&2
      echo "Every shipped fixture declares at least one module; an empty tree means the CLI's output shape has changed." >&2
      exit 1
    }

    while IFS= read -r module_dir; do
      module="$(basename "$module_dir")"
      if [[ -f "$STAGE/module-owners/$module" ]]; then
        owner="$(<"$STAGE/module-owners/$module")"
        echo "regenerate.sh: module folder $module is emitted by both $owner and $package." >&2
        echo "Two conformance packages cannot share a Daml module name: their generated trees would overwrite each other." >&2
        exit 1
      fi
      printf '%s' "$package" > "$STAGE/module-owners/$module"
      mv "$module_dir" "$STAGE/out/$CONFORMANCE_GENERATED_REL/$module"
      if [[ "$PARTIAL_CONFORMANCE" == true ]]; then
        claim conformance "$CONFORMANCE_GENERATED_REL/$module"
      fi
    done < <(find "$emitted_root" -mindepth 1 -maxdepth 1 -type d | LC_ALL=C sort)

    rm -rf "${raw_dir:?}/${CONFORMANCE_ROOT_NAMESPACE%%.*}"
    leftover="$(find "$raw_dir" -mindepth 1 -type f)"
    if [[ -n "$leftover" ]]; then
      echo "regenerate.sh: unexpected emitter output outside the pinned namespace tree for $package:" >&2
      echo "$leftover" >&2
      echo "The CLI's output shape has changed; update the conformance module-folder step to match." >&2
      exit 1
    fi
  done

  if [[ "$PARTIAL_CONFORMANCE" != true ]]; then
    claim conformance "$CONFORMANCE_GENERATED_REL"
  fi
}

run_determinism_pipeline() {
  local run_dir="$1"
  mkdir -p "$run_dir/out"
  "$JAVA_BIN" -jar "$HELPER_JAR" --dar "$PROJECT_ROOT/$DETERMINISM_DAR_REL" --out "$run_dir/intermediate.binpb" >/dev/null || return 1
  "$CLI_BIN" --intermediate "$run_dir/intermediate.binpb" -o "$run_dir/out" "${PINNED_EMITTER_ARGS[@]}" >/dev/null || return 1
}

build_cs_manifest() {
  local out_dir="$1" manifest_path="$2" relpath hash
  : > "$manifest_path"
  while IFS= read -r relpath; do
    hash="$(sha256_of "$out_dir/$relpath")"
    printf '%s  %s\n' "$hash" "$relpath" >> "$manifest_path"
  done < <(cd "$out_dir" && find . -type f -name '*.cs' -print | sed 's|^\./||' | LC_ALL=C sort)

  [[ -s "$manifest_path" ]] || {
    echo "regenerate.sh: the emitter produced no .cs files under $out_dir" >&2
    echo "The pinned fixture is type-bearing; refusing to pin an empty tree." >&2
    exit 1
  }
}

produce_determinism() {
  local run_dir="$STAGE/determinism/run1" shas_dir="$STAGE/out/$DETERMINISM_SHAS_REL"
  [[ -f "$PROJECT_ROOT/$DETERMINISM_DAR_REL" ]] || die "fixture DAR not found: $DETERMINISM_DAR_REL"
  run_determinism_pipeline "$run_dir" || die "the determinism pipeline failed on $DETERMINISM_DAR_REL"
  mkdir -p "$shas_dir"
  sha256_of "$run_dir/intermediate.binpb" | awk '{printf "%s  intermediate.binpb\n", $1}' > "$shas_dir/intermediate.sha256"
  build_cs_manifest "$run_dir/out" "$shas_dir/cs-tree.manifest"
  sha256_of "$shas_dir/cs-tree.manifest" | awk '{printf "%s  cs-tree.manifest\n", $1}' > "$shas_dir/cs-tree.sha256"
  claim determinism "$DETERMINISM_SHAS_REL/intermediate.sha256"
  claim determinism "$DETERMINISM_SHAS_REL/cs-tree.manifest"
  claim determinism "$DETERMINISM_SHAS_REL/cs-tree.sha256"
}

repeat_determinism() {
  local first="$STAGE/determinism/run1" second="$STAGE/determinism/run2" repeated=0
  run_determinism_pipeline "$second" || { echo "::error::The determinism pipeline failed on its second run." >&2; return 1; }

  if [[ "$(sha256_of "$first/intermediate.binpb")" != "$(sha256_of "$second/intermediate.binpb")" ]]; then
    echo "::error::Non-determinism detected in the JVM helper: two runs against the same fixture DAR produced different IntermediateDar proto bytes. This is a real bug in AstToIntermediate, not a pinned SHA to refresh." >&2
    repeated=1
  fi

  build_cs_manifest "$first/out" "$STAGE/determinism/manifest1.txt" || return 1
  build_cs_manifest "$second/out" "$STAGE/determinism/manifest2.txt" || return 1
  if ! diff -q "$STAGE/determinism/manifest1.txt" "$STAGE/determinism/manifest2.txt" >/dev/null; then
    echo "::error::Non-determinism detected in the C# emitter: two runs against the same IntermediateDar produced a different .cs tree. This is a real bug in the emitter, not a pinned SHA to refresh." >&2
    diff -u "$STAGE/determinism/manifest1.txt" "$STAGE/determinism/manifest2.txt" >&2 || true
    repeated=1
  fi
  return "$repeated"
}

replace_in_place() {
  local rel="$1"
  rm -rf "${PROJECT_ROOT:?}/$rel"
  mkdir -p "$(dirname "$PROJECT_ROOT/$rel")"
  cp -R "$STAGE/out/$rel" "$PROJECT_ROOT/$rel"
}

write_claims() {
  local entry rel
  while IFS=$'\t' read -r entry rel; do
    replace_in_place "$rel"
  done < "$STAGE/claims"
}

report_drift() {
  local entry="$1" rel="$2" detail="$STAGE/drift-detail.txt"
  echo "::error::$rel drifted from what the pipeline regenerates (entry '$entry'). Run scripts/regenerate.sh write --only $entry and commit the result." >&2
  if [[ -e "$PROJECT_ROOT/$rel" ]]; then
    diff -ru "$STAGE/out/$rel" "$PROJECT_ROOT/$rel" > "$detail" 2>&1 || true
    sed -e "s|$STAGE/out/|regenerated/|g" -e "s|$PROJECT_ROOT/|committed/|g" "$detail" | head -n 60 >&2 || true
  else
    echo "  $rel is missing from the checkout." >&2
  fi
}

check_claims() {
  local entry rel drifted=0
  while IFS=$'\t' read -r entry rel; do
    if [[ -e "$PROJECT_ROOT/$rel" ]] && diff -r "$STAGE/out/$rel" "$PROJECT_ROOT/$rel" >/dev/null 2>&1; then
      continue
    fi
    report_drift "$entry" "$rel"
    drifted=1
  done < "$STAGE/claims"
  return "$drifted"
}

STAGE="$(mktemp -d)"
cleanup_stage() { rm -rf "$STAGE"; }
trap cleanup_stage EXIT
trap 'cleanup_stage; exit 130' INT
trap 'cleanup_stage; exit 143' TERM
mkdir -p "$STAGE/out" "$STAGE/protos"
: > "$STAGE/claims"

prepare_tools

for entry in $SELECTED; do
  echo "regenerate.sh: producing $entry"
  "$(function_name_of produce "$entry")"
done

if [[ "$MODE" == "write" ]]; then
  write_claims
  echo ""
  echo "regenerate.sh: wrote$SELECTED. Review with: git status --short"
  exit 0
fi

FAILED=0
check_claims || FAILED=1
for entry in $SELECTED; do
  if [[ "$(type -t "$(function_name_of repeat "$entry")")" == "function" ]]; then
    echo "regenerate.sh: running $entry a second time"
    "$(function_name_of repeat "$entry")" || FAILED=1
  fi
done

if [[ "$FAILED" -ne 0 ]]; then
  exit 1
fi

echo "regenerate.sh: OK —$SELECTED regenerate byte-identically to the checked-in artefacts, and repeat runs agree."
