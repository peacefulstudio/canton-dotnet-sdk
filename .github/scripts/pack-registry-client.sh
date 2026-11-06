#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Pack the Splice Token Standard registry client beside the DAR-generated Splice
packages. The client version is <CLIENT_BASE_VERSION>.<g>[-prerelease]: g and the
prerelease suffix are those the DAR packages of this run carry, so one release has
one ordinal and one suffix. The project is skipped with a notice when the release
tag does not contain it, and nothing is packed when CLIENT_PROJECT is empty.

Reads (env):
  CLIENT_PROJECT       Path of the client csproj, relative to the checkout root; empty = nothing to pack.
  CLIENT_BASE_VERSION  Splice release the client is generated from (M.m.p).
  NUPKG_DIR            Directory of the DAR packages; the client nupkg is written here too.
  LOCAL_FEED           Optional local feed directory; the client nupkg is copied here too.
  RUNTIME_VERSION      SDK version passed as Version, which sets the Daml.Runtime dependency floor.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

if [ -z "${CLIENT_PROJECT:-}" ]; then
  echo "No registry client project configured; nothing to pack."
  exit 0
fi

if [ ! -f "$CLIENT_PROJECT" ]; then
  echo "::notice::$CLIENT_PROJECT is not in this release tag; the registry client is not packed."
  exit 0
fi

shopt -s nullglob
ordinals_and_suffixes=()
for pkg in "$NUPKG_DIR"/*.nupkg; do
  version="$(unzip -p "$pkg" '*.nuspec' | sed -n 's|.*<version>\([^<]*\)</version>.*|\1|p' | head -n 1)"
  version="${version%%+*}"
  suffix=""
  case "$version" in *-*) suffix="-${version#*-}" ;; esac
  IFS=. read -r _ _ _ ordinal <<< "${version%%-*}"
  ordinals_and_suffixes+=("${ordinal:-0}${suffix}")
done

if [ "${#ordinals_and_suffixes[@]}" -eq 0 ]; then
  echo "::error::No DAR packages in $NUPKG_DIR; cannot tell which ordinal the registry client takes."
  exit 1
fi

mapfile -t distinct < <(printf '%s\n' "${ordinals_and_suffixes[@]}" | sort -u)
if [ "${#distinct[@]}" -ne 1 ]; then
  echo "::error::DAR packages carry more than one ordinal or prerelease suffix (${distinct[*]}); the registry client takes exactly one."
  exit 1
fi

client_version="${CLIENT_BASE_VERSION}.${distinct[0]}"
echo "Packing $CLIENT_PROJECT as $client_version (Version=$RUNTIME_VERSION)"
dotnet pack "$CLIENT_PROJECT" -c Release -o "$NUPKG_DIR" \
  -p:SpliceRegistryClientVersion="$client_version" \
  -p:Version="$RUNTIME_VERSION"

if [ -n "${LOCAL_FEED:-}" ]; then
  mkdir -p "$LOCAL_FEED"
  cp "$NUPKG_DIR"/Splice.Api.Token.Registry.Client."$client_version".nupkg "$LOCAL_FEED/"
fi
