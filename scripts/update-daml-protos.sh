#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

# Vendors the Daml-LF archive protos from the com.daml:daml-lf-archive_2.13 jar on Maven Central,
# the artifact the JVM helper decodes with. The digital-asset/daml GitHub repo has no tag past
# v3.4.11 and com.daml:daml-lf-archive-proto stops at 3.5.0 snapshots, so neither serves 3.6.x.
# The jar is checked against a SHA-256 the caller pins, obtained apart from the download, before
# anything is unpacked: the extracted protos are promoted to the public mirror.
# Usage: ./scripts/update-daml-protos.sh <daml-lf-archive-version> <jar-sha256>
# Example: ./scripts/update-daml-protos.sh 3.6.4 <64 lowercase hex digits>

MAVEN_BASE="${DAML_LF_ARCHIVE_MAVEN_BASE:-https://repo1.maven.org/maven2/com/daml/daml-lf-archive_2.13}"
PROTO_FILES=(daml_lf.proto daml_lf1.proto daml_lf2.proto)
PROTO_JAR_DIR="com/digitalasset/daml/lf/archive"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TARGET_DIR="$PROJECT_ROOT/src/Daml.Codegen.DarParser/Proto/$PROTO_JAR_DIR"

usage_error() {
  echo "Usage: $0 <daml-lf-archive-version> <jar-sha256>" >&2
  echo "Example: $0 3.6.4 <64 lowercase hex digits>" >&2
  echo "Error: $1" >&2
  exit 2
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

[[ $# -eq 2 ]] || usage_error "expected <daml-lf-archive-version> and <jar-sha256>, got $# argument(s)."

VERSION="$1"
EXPECTED_SHA256="$2"
[[ "$EXPECTED_SHA256" =~ ^[0-9a-f]{64}$ ]] || usage_error "'$EXPECTED_SHA256' is not a SHA-256 (64 lowercase hex digits)."
JAR_NAME="daml-lf-archive_2.13-$VERSION.jar"
JAR_URL="$MAVEN_BASE/$VERSION/$JAR_NAME"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

echo "Updating Daml-LF protos from $JAR_URL"

if ! curl --silent --show-error --fail --location --connect-timeout 15 --max-time 300 \
  "$JAR_URL" -o "$WORK_DIR/archive.jar"; then
  echo "Error: could not download $JAR_URL" >&2
  echo "Check that daml-lf-archive_2.13 $VERSION exists on Maven Central." >&2
  exit 1
fi

ACTUAL_SHA256="$(sha256_of "$WORK_DIR/archive.jar")"
if [[ "$ACTUAL_SHA256" != "$EXPECTED_SHA256" ]]; then
  echo "Error: sha256 mismatch for $JAR_NAME: expected $EXPECTED_SHA256, got $ACTUAL_SHA256" >&2
  exit 1
fi

for file in "${PROTO_FILES[@]}"; do
  unzip -q -o -j "$WORK_DIR/archive.jar" "$PROTO_JAR_DIR/$file" -d "$WORK_DIR/protos"
done

mkdir -p "$TARGET_DIR"

for file in "${PROTO_FILES[@]}"; do
  cp "$WORK_DIR/protos/$file" "$TARGET_DIR/$file"

  # Workaround: upstream mixes Com.Digitalasset and Com.DigitalAsset in csharp_namespace, and
  # mixed casing breaks the build with CA1708. BSD sed needs the -i.bak suffix, GNU sed accepts it.
  sed -i.bak 's/Com\.Digitalasset\./Com.DigitalAsset./g' "$TARGET_DIR/$file"
  rm -f "$TARGET_DIR/$file.bak"
done

cat > "$TARGET_DIR/.daml-lf-proto-version" <<EOT
$VERSION
EOT

echo ""
echo "Done. Proto files updated to daml-lf-archive $VERSION"
echo "  Source: $JAR_URL (sha256 $ACTUAL_SHA256)"
echo "  Target: $TARGET_DIR/"
echo ""
echo "Next steps:"
echo "  1. Review changes: git diff src/Daml.Codegen.DarParser/Proto/"
echo "  2. Build to regenerate protobuf stubs: dotnet build"
echo "  3. Fix any breaking changes in DalfReader.cs / PackageContext.cs"
