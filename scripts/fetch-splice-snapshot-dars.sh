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
download of <base-url>/<commit>/daml/dars/<file>, bounded by a 15 s connect
timeout and a 300 s overall timeout and retried 3 times on any error. A download
whose sha256 differs from the manifest is discarded and fails the run.

Concurrent runs on one cache directory are serialized on the <cache-dir>/.lock
directory, held for the whole run; a lock whose owner process has exited is
broken.

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
lock_dir="${cache_dir}/.lock"

release_lock() {
  rm -rf "${lock_dir}"
}

acquire_lock() {
  local owner
  until mkdir "${lock_dir}" 2>/dev/null; do
    if [ ! -d "${lock_dir}" ]; then
      mkdir "${lock_dir}" 2>/dev/null && break
      [ -d "${lock_dir}" ] || broken "cannot create the lock ${lock_dir}."
    fi
    owner="$(cat "${lock_dir}/pid" 2>/dev/null || true)"
    if [ -n "${owner}" ] && ! kill -0 "${owner}" 2>/dev/null; then
      echo "breaking the lock left by exited run ${owner}" >&2
      release_lock
      continue
    fi
    sleep 1
  done
  trap release_lock EXIT
  trap 'exit 1' HUP INT TERM
  printf '%s\n' "$$" >"${lock_dir}/pid"
}

acquire_lock

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
  if ! curl --fail --silent --show-error --location \
    --connect-timeout 15 --max-time 300 --retry 3 --retry-all-errors \
    --output "${partial}" "${url}"; then
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
