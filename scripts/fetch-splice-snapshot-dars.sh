#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

EXIT_BROKEN=2
DEFAULT_BASE_URL="https://raw.githubusercontent.com/canton-network/splice"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(cd "${script_dir}/.." && pwd)"
manifest="${project_root}/tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-dars.manifest"
cache_dir="${SPLICE_SNAPSHOT_DARS_DIR:-${project_root}/output/splice-snapshot-dars}"

usage() {
  cat <<EOF
Usage: $(basename "${BASH_SOURCE[0]}") [--manifest <path>] [--dir <cache-dir>]

Make the Splice DARs the codegen snapshots were generated from available as
<cache-dir>/<snapshot>.dar, and print <cache-dir>. The DARs are not vendored: the
manifest (default: tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-dars.manifest)
pins an upstream commit and, per snapshot, the DAR's upstream file name and
sha256. A cached DAR whose sha256 matches is kept; any other is replaced by a
download of <base-url>/<commit>/daml/dars/<file>. A download whose sha256 differs
from the manifest is discarded and fails the run.

Options:
  --manifest <path>  the manifest to read.
  --dir <cache-dir>  the cache directory (default: \$SPLICE_SNAPSHOT_DARS_DIR, else
                     output/splice-snapshot-dars under the repository root).

Reads (env): SPLICE_SNAPSHOT_DARS_BASE_URL  download base URL (default: ${DEFAULT_BASE_URL};
             tests point it at a file:// directory).
             SPLICE_SNAPSHOT_DARS_DIR       the cache directory.

Manifest lines: "commit <40 lowercase hex>", then one
"<snapshot> <upstream file name> <64 lowercase hex sha256>" per DAR.

Exit codes: 0 every DAR is cached and verified, 1 a DAR could not be fetched or
failed its digest, ${EXIT_BROKEN} the manifest is unusable.
EOF
}

broken() {
  echo "::error::splice snapshot DARs: $*" >&2
  exit "${EXIT_BROKEN}"
}

failed() {
  echo "::error::splice snapshot DARs: $*" >&2
  exit 1
}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    --manifest)
      [ "$#" -ge 2 ] || broken "--manifest takes a path."
      manifest="$2"
      shift 2
      ;;
    --dir)
      [ "$#" -ge 2 ] || broken "--dir takes a directory."
      cache_dir="$2"
      shift 2
      ;;
    *)
      broken "unknown argument: $1"
      ;;
  esac
done

[ -f "${manifest}" ] || broken "no manifest at ${manifest}."

commit=""
entries=()
while read -r first second third extra; do
  [ -n "${first}" ] || continue
  if [ "${first}" = "commit" ]; then
    [[ "${second:-}" =~ ^[0-9a-f]{40}$ && -z "${third:-}" ]] || broken "the commit line must carry 40 lowercase hex digits."
    commit="${second}"
    continue
  fi
  [[ "${first}" =~ ^[a-z0-9-]+$ ]] || broken "'${first}' is not a snapshot name."
  [[ "${second:-}" =~ ^[A-Za-z0-9._-]+\.dar$ ]] || broken "${first}: '${second:-}' is not a DAR file name."
  [[ "${third:-}" =~ ^[0-9a-f]{64}$ && -z "${extra:-}" ]] || broken "${first}: the digest must be 64 lowercase hex digits."
  entries+=("${first} ${second} ${third}")
done <"${manifest}"

[ -n "${commit}" ] || broken "${manifest} has no commit line."
[ "${#entries[@]}" -gt 0 ] || broken "${manifest} lists no DAR."

base_url="${SPLICE_SNAPSHOT_DARS_BASE_URL:-${DEFAULT_BASE_URL}}"
mkdir -p "${cache_dir}"

for entry in "${entries[@]}"; do
  read -r snapshot upstream_name expected <<<"${entry}"
  cached="${cache_dir}/${snapshot}.dar"

  if [ -f "${cached}" ] && [ "$(sha256_of "${cached}")" = "${expected}" ]; then
    continue
  fi
  rm -f "${cached}"

  partial="$(mktemp "${cache_dir}/.partial.XXXXXX")"
  url="${base_url}/${commit}/daml/dars/${upstream_name}"
  echo "fetching ${snapshot} from ${url}" >&2
  if ! curl --fail --silent --show-error --location --output "${partial}" "${url}"; then
    rm -f "${partial}"
    failed "${snapshot}: could not download ${url}."
  fi

  actual="$(sha256_of "${partial}")"
  if [ "${actual}" != "${expected}" ]; then
    rm -f "${partial}"
    failed "${snapshot}: digest mismatch for ${upstream_name}: the manifest pins ${expected}, the download is ${actual}."
  fi
  mv "${partial}" "${cached}"
done

printf '%s\n' "${cache_dir}"
