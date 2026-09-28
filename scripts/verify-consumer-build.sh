#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 --dar <path.dar>[,<path.dar>...] | --intermediate <path.binpb>[,...]
          [--out <work-dir>] [--feed <feed-dir>] [--consume <package-id>]
          [--runtime-version <ver>] [--package-license <spdx>]
          [--helper-jar <path>] [--cli-bin <path>] [--keep]

End-to-end consumer-build proof for generated C#. For each input archive it:

  1. emits C# (JVM helper .dar -> .binpb when given --dar, then the CLI with
     --generate-project), greps the emitted .cs for unresolved-stdlib leaks
     (No.Package.Metadata.*),
  2. writes <out>/NuGet.config (maps Daml.*, Splice.* and every generated
     package id to the local feed) and <out>/Directory.Build.props (opts out
     of Central Package Management so an --out inside the repo cannot leak it),
  3. packs Daml.Runtime + Daml.Ledger.Abstractions and every generated .csproj
     (inputs are packed in the order given, so list a closure dependencies-first)
     into a local NuGet feed,
  4. scaffolds a fresh 'dotnet new console', 'dotnet add package's the chosen
     consume target, and 'dotnet build's it to PROVE the generated code
     compiles for a downstream consumer.

Inputs (one of --dar / --intermediate is required; both may be given):
  --dar <list>            Comma-separated .dar archives. Each is decoded by the
                          JVM helper into a .binpb, then emitted.
  --intermediate <list>   Comma-separated pre-decoded IntermediateDar .binpb
                          files. Emitted directly (no JVM helper needed).

Options:
  --out <dir>             Work dir for emitted C# + scaffolded consumer
                          (default: \$VERIFY_OUT or ./output/verify-consumer).
  --feed <dir>            Local NuGet feed dir for packed .nupkg
                          (default: <out>/feed).
  --consume <package-id>  Package id the consumer 'dotnet add package's. Default
                          is the package generated from the LAST input (the top
                          of a dependencies-first closure).
  --runtime-version <ver> Daml.Runtime version stamped into generated .csproj and
                          packed runtime packages (default: \$RUNTIME_VERSION or
                          read from Directory.Build.props <Version>).
  --package-license <spdx> SPDX license stamped into generated packages
                          (default: \$PACKAGE_LICENSE, else unset).
  --helper-jar <path>     daml-dar-to-proto.jar (only needed for --dar
                          inputs). Overriding this skips the freshness check; you
                          own the JAR's currency. Default: the repo JAR,
                          reassembled from source when stale via
                          scripts/ensure-jvm-helper-jar.sh
  --cli-bin <path>        Daml.Codegen.CSharp.Cli binary. Default:
                          <repo>/src/Daml.Codegen.CSharp.Cli/bin/Release/net10.0/Daml.Codegen.CSharp.Cli
  --keep                  Do not wipe <out> before running.
EOF
  exit "${1:-1}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DAR_LIST=""
INTERMEDIATE_LIST=""
OUT="${VERIFY_OUT:-$REPO_ROOT/output/verify-consumer}"
FEED=""
CONSUME=""
RUNTIME_VERSION="${RUNTIME_VERSION:-}"
PACKAGE_LICENSE="${PACKAGE_LICENSE:-}"
HELPER_JAR=""
CLI_BIN="$REPO_ROOT/src/Daml.Codegen.CSharp.Cli/bin/Release/net10.0/Daml.Codegen.CSharp.Cli"
KEEP=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dar) DAR_LIST="$2"; shift 2 ;;
    --intermediate) INTERMEDIATE_LIST="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --feed) FEED="$2"; shift 2 ;;
    --consume) CONSUME="$2"; shift 2 ;;
    --runtime-version) RUNTIME_VERSION="$2"; shift 2 ;;
    --package-license) PACKAGE_LICENSE="$2"; shift 2 ;;
    --helper-jar) HELPER_JAR="$2"; shift 2 ;;
    --cli-bin) CLI_BIN="$2"; shift 2 ;;
    --keep) KEEP="1"; shift 1 ;;
    -h|--help) usage 0 ;;
    *) echo "verify-consumer-build.sh: unknown arg: $1" >&2; usage ;;
  esac
done

[[ -n "$DAR_LIST" || -n "$INTERMEDIATE_LIST" ]] || { echo "verify-consumer-build.sh: one of --dar / --intermediate is required" >&2; usage; }
FEED="${FEED:-$OUT/feed}"

command -v dotnet >/dev/null 2>&1 || { echo "verify-consumer-build.sh: 'dotnet' not found on PATH (.NET SDK required)" >&2; exit 1; }
[[ -x "$CLI_BIN" ]] || {
  echo "verify-consumer-build.sh: CLI binary not found or not executable: $CLI_BIN" >&2
  echo "Build it with: dotnet build src/Daml.Codegen.CSharp.Cli -c Release" >&2
  exit 1
}

if [[ -z "$RUNTIME_VERSION" ]]; then
  RUNTIME_VERSION="$(grep -oE '<Version>[^<]+</Version>' "$REPO_ROOT/Directory.Build.props" 2>/dev/null | head -1 | sed -E 's/<\/?Version>//g' || true)"
  [[ -n "$RUNTIME_VERSION" ]] || { echo "verify-consumer-build.sh: could not derive --runtime-version from Directory.Build.props; pass it explicitly" >&2; exit 1; }
fi

split_csv() { local IFS=','; read -ra _PARTS <<<"$1"; printf '%s\n' "${_PARTS[@]}"; }
xml_escape() { sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g' -e "s/'/\&apos;/g"; }

EMIT_ROOT="$OUT/emitted"
CONSUMER_DIR="$OUT/consumer"
NUGET_CONFIG="$OUT/NuGet.config"

if [[ -z "$KEEP" ]]; then
  rm -rf "$OUT"
fi
export NUGET_PACKAGES="$OUT/.nuget-packages"
rm -rf "$NUGET_PACKAGES"
mkdir -p "$EMIT_ROOT" "$FEED" "$NUGET_PACKAGES"

derive_csproj() {
  find "$1" -maxdepth 2 -name '*.csproj' | head -1
}

find_unresolved_stdlib_leaks() {
  grep -rhoE 'No\.Package\.Metadata\.[A-Za-z0-9_]+' "$1" --include='*.cs' 2>/dev/null | sort -u | tr '\n' ' ' || true
}

EMITTED_PROJECT_DIRS=()

emit_from_intermediate() {
  local binpb="$1" name="$2"
  local dest="$EMIT_ROOT/$name"
  echo "  emit  $name"
  local cli_args=(--intermediate "$binpb" -o "$dest" --generate-project --runtime-version "$RUNTIME_VERSION" --verbosity 0)
  [[ -n "$PACKAGE_LICENSE" ]] && cli_args+=(--package-license "$PACKAGE_LICENSE")
  "$CLI_BIN" "${cli_args[@]}"
  local cs_count leaks
  cs_count="$(find "$dest" -name '*.cs' 2>/dev/null | wc -l | tr -d ' ')"
  leaks="$(find_unresolved_stdlib_leaks "$dest")"
  echo "        cs=$cs_count leaks=[$leaks]"
  [[ -z "$leaks" ]] || { echo "verify-consumer-build.sh: unresolved-stdlib leaks emitted for $name: $leaks" >&2; exit 4; }
  EMITTED_PROJECT_DIRS+=("$dest")
}

echo "=== emit (runtime-version=$RUNTIME_VERSION${PACKAGE_LICENSE:+, license=$PACKAGE_LICENSE}) ==="

if [[ -n "$INTERMEDIATE_LIST" ]]; then
  while IFS= read -r binpb; do
    [[ -n "$binpb" ]] || continue
    [[ -f "$binpb" ]] || { echo "verify-consumer-build.sh: intermediate not found: $binpb" >&2; exit 1; }
    emit_from_intermediate "$binpb" "$(basename "$binpb" .binpb)"
  done < <(split_csv "$INTERMEDIATE_LIST")
fi

if [[ -n "$DAR_LIST" ]]; then
  if [[ -z "$HELPER_JAR" ]]; then
    HELPER_JAR="$("$SCRIPT_DIR/ensure-jvm-helper-jar.sh")"
  else
    [[ -f "$HELPER_JAR" ]] || {
      echo "verify-consumer-build.sh: JVM helper JAR not found: $HELPER_JAR" >&2
      echo "Build it with: scripts/ensure-jvm-helper-jar.sh" >&2
      exit 1
    }
  fi
  command -v java >/dev/null 2>&1 || { echo "verify-consumer-build.sh: 'java' not found on PATH (required for --dar inputs)" >&2; exit 1; }
  BINPB_DIR="$OUT/binpb"
  mkdir -p "$BINPB_DIR"
  while IFS= read -r dar; do
    [[ -n "$dar" ]] || continue
    [[ -f "$dar" ]] || { echo "verify-consumer-build.sh: DAR not found: $dar" >&2; exit 1; }
    name="$(basename "$dar" .dar)"
    echo "  decode $name"
    java -jar "$HELPER_JAR" --dar "$dar" --out "$BINPB_DIR/$name.binpb"
    emit_from_intermediate "$BINPB_DIR/$name.binpb" "$name"
  done < <(split_csv "$DAR_LIST")
fi

[[ ${#EMITTED_PROJECT_DIRS[@]} -gt 0 ]] || { echo "verify-consumer-build.sh: nothing emitted" >&2; exit 1; }

declare -a EMITTED_CSPROJS=() CONSUMABLE_PACKAGE_IDS=()
for project_dir in "${EMITTED_PROJECT_DIRS[@]}"; do
  csproj="$(derive_csproj "$project_dir")"
  [[ -n "$csproj" ]] || { echo "verify-consumer-build.sh: no .csproj under $project_dir" >&2; exit 1; }
  EMITTED_CSPROJS+=("$csproj")
  CONSUMABLE_PACKAGE_IDS+=("$(basename "$csproj" .csproj)")
done

# The emitted projects and the consumer console live under $OUT, so their
# NuGet.config and Directory.Build.props must exist BEFORE the first pack:
# otherwise the emitted csproj resolves Daml.Runtime from nuget.org, whose
# released runtime lacks the unreleased types the current emitter emits, and a
# --out inside the repo leaks the repo's Central Package Management (NU1008) and
# its Directory.Build.props into them. The nearest files win the upward walk.
FEED_XML="$(printf '%s' "$FEED" | xml_escape)"
GENERATED_PATTERNS=""
for pkg_id in "${CONSUMABLE_PACKAGE_IDS[@]}"; do
  GENERATED_PATTERNS+="      <package pattern=\"$(printf '%s' "$pkg_id" | xml_escape)\" />"$'\n'
done

cat >"$NUGET_CONFIG" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="$FEED_XML" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-feed">
      <package pattern="Daml.*" />
      <package pattern="Splice.*" />
$GENERATED_PATTERNS    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

cat >"$OUT/Directory.Build.props" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps>
  </PropertyGroup>
</Project>
XML

echo "=== pack runtime + generated packages into $FEED ==="
for runtime_proj in \
  "$REPO_ROOT/src/Daml.Runtime/Daml.Runtime.csproj" \
  "$REPO_ROOT/src/Daml.Ledger.Abstractions/Daml.Ledger.Abstractions.csproj"; do
  [[ -f "$runtime_proj" ]] || { echo "verify-consumer-build.sh: runtime project not found: $runtime_proj" >&2; exit 1; }
  echo "  pack  $(basename "$runtime_proj" .csproj)"
  dotnet pack "$runtime_proj" -c Release -o "$FEED" /p:Version="$RUNTIME_VERSION"
done

for csproj in "${EMITTED_CSPROJS[@]}"; do
  echo "  pack  $(basename "$csproj" .csproj)"
  dotnet pack "$csproj" -c Release -o "$FEED"
done

echo "=== feed contents ==="
ls -1 "$FEED"

[[ -n "$CONSUME" ]] || CONSUME="${CONSUMABLE_PACKAGE_IDS[${#CONSUMABLE_PACKAGE_IDS[@]}-1]}"

echo "=== fresh consumer: dotnet new console + add $CONSUME + build ==="
rm -rf "$CONSUMER_DIR"
mkdir -p "$CONSUMER_DIR"
cp "$NUGET_CONFIG" "$CONSUMER_DIR/NuGet.config"
dotnet new console -o "$CONSUMER_DIR" --force
dotnet add "$CONSUMER_DIR" package "$CONSUME"
dotnet build "$CONSUMER_DIR" -c Release

echo "Consumer build succeeded: $CONSUME compiles against the local feed."
echo "Done."
