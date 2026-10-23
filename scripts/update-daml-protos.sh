#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

# Vendors the Daml-LF archive protos from the com.daml:daml-lf-archive_2.13 jar on Maven Central,
# the artifact the JVM helper decodes with. The digital-asset/daml GitHub repo has no tag past
# v3.4.11 and com.daml:daml-lf-archive-proto stops at 3.5.0 snapshots, so neither serves 3.6.x.
# Usage: ./scripts/update-daml-protos.sh <daml-lf-archive-version>
# Example: ./scripts/update-daml-protos.sh 3.6.1

MAVEN_BASE="https://repo1.maven.org/maven2/com/daml/daml-lf-archive_2.13"
PROTO_FILES=(daml_lf.proto daml_lf1.proto daml_lf2.proto)
PROTO_JAR_DIR="com/digitalasset/daml/lf/archive"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TARGET_DIR="$PROJECT_ROOT/src/Daml.Codegen.DarParser/Proto/$PROTO_JAR_DIR"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <daml-lf-archive-version>"
  echo "Example: $0 3.6.1"
  exit 1
fi

VERSION="$1"
JAR_URL="$MAVEN_BASE/$VERSION/daml-lf-archive_2.13-$VERSION.jar"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

echo "Updating Daml-LF protos from $JAR_URL"

if ! curl --silent --fail --location "$JAR_URL" -o "$WORK_DIR/archive.jar"; then
  echo "Error: could not download $JAR_URL"
  echo "Check that daml-lf-archive_2.13 $VERSION exists on Maven Central."
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
echo "  Source: $JAR_URL"
echo "  Target: $TARGET_DIR/"
echo ""
echo "Next steps:"
echo "  1. Review changes: git diff src/Daml.Codegen.DarParser/Proto/"
echo "  2. Build to regenerate protobuf stubs: dotnet build"
echo "  3. Fix any breaking changes in DalfReader.cs / PackageContext.cs"
