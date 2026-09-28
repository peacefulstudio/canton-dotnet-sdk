#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 <nupkg-dir> [--push <source>]

Compute — and optionally run — the dependency-ordered nuget.org push for the
16 packages published by this repository's ledger fold-in publish cutover.

  1. Fails unless the set of package ids under <nupkg-dir> is EXACTLY the
     list in scripts/published-package-ids.txt (no missing id, no extra
     package), naming the difference.
  2. Reads each package's nuspec <dependency id> entries and topologically
     sorts the packed set, dependencies first, breaking ties alphabetically
     by package id. Fails on a dependency cycle. A dependency on an id
     outside the published set (e.g. a third-party package) is ignored for
     ordering purposes.
  3. Prints the ordered .nupkg paths, one per line, to stdout.
  4. With --push <source>, additionally pushes each package to <source> in
     that order via 'dotnet nuget push --skip-duplicate', stopping at the
     first failure so that every package already pushed stays a
     dependency-complete prefix. Reads the api key from the NUGET_API_KEY
     environment variable, which must be set when --push is used.

Exit codes: 0 ordered (and pushed, with --push); 1 the packed set or the
dependency graph is invalid, or a push failed; 2 usage or environment error.
EOF
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
esac

if [ $# -lt 1 ]; then
  usage >&2
  exit 2
fi

nupkg_dir="$1"
shift

push_source=""
if [ $# -gt 0 ]; then
  if [ "$1" != "--push" ]; then
    usage >&2
    exit 2
  fi
  push_source="${2:-}"
  if [ -z "$push_source" ]; then
    usage >&2
    exit 2
  fi
  shift 2
fi

if [ $# -ne 0 ]; then
  usage >&2
  exit 2
fi

if [ ! -d "$nupkg_dir" ]; then
  echo "order-nuget-push.sh: not a directory: $nupkg_dir" >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ids_file="$script_dir/published-package-ids.txt"

if [ ! -f "$ids_file" ]; then
  echo "order-nuget-push.sh: missing $ids_file" >&2
  exit 2
fi

if [ -n "$push_source" ] && [ -z "${NUGET_API_KEY:-}" ]; then
  echo "order-nuget-push.sh: --push requires the NUGET_API_KEY environment variable" >&2
  exit 2
fi

ordered_paths="$(NUPKG_DIR="$nupkg_dir" IDS_FILE="$ids_file" python3 - <<'PY'
import glob
import os
import sys
import xml.etree.ElementTree as ET
import zipfile


def fail(message, code=1):
    print(f"order-nuget-push.sh: {message}", file=sys.stderr)
    sys.exit(code)


def local_name(tag):
    return tag.split("}", 1)[-1] if "}" in tag else tag


def read_nuspec(path):
    with zipfile.ZipFile(path) as archive:
        nuspec_names = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
        if len(nuspec_names) != 1:
            fail(f"{path}: expected exactly one .nuspec entry, found {len(nuspec_names)}", 2)
        with archive.open(nuspec_names[0]) as handle:
            root = ET.parse(handle).getroot()

    metadata = next((child for child in root if local_name(child.tag) == "metadata"), None)
    if metadata is None:
        fail(f"{path}: nuspec has no <metadata>", 2)

    id_element = next((child for child in metadata if local_name(child.tag) == "id"), None)
    package_id = (id_element.text or "").strip() if id_element is not None else ""
    if not package_id:
        fail(f"{path}: nuspec <metadata> has no <id>", 2)

    dependencies = set()
    dependencies_element = next(
        (child for child in metadata if local_name(child.tag) == "dependencies"), None
    )
    if dependencies_element is not None:
        for node in dependencies_element.iter():
            if local_name(node.tag) == "dependency":
                dependency_id = node.attrib.get("id", "").strip()
                if dependency_id:
                    dependencies.add(dependency_id)

    return package_id, dependencies


ids_file = os.environ["IDS_FILE"]
nupkg_dir = os.environ["NUPKG_DIR"]

expected_ids = [line.strip() for line in open(ids_file, encoding="utf-8") if line.strip()]
expected_set = set(expected_ids)
if len(expected_ids) != len(expected_set):
    fail(f"{ids_file} lists a duplicate package id", 2)

nupkg_paths = sorted(glob.glob(os.path.join(nupkg_dir, "*.nupkg")))
if not nupkg_paths:
    fail(f"no .nupkg files found in {nupkg_dir}", 2)

path_by_id = {}
deps_by_id = {}
for path in nupkg_paths:
    package_id, dependencies = read_nuspec(path)
    if package_id in path_by_id:
        fail(f"duplicate package id {package_id}: {path_by_id[package_id]} and {path}", 2)
    path_by_id[package_id] = path
    deps_by_id[package_id] = dependencies

actual_set = set(path_by_id)
missing = sorted(expected_set - actual_set)
extra = sorted(actual_set - expected_set)
if missing or extra:
    lines = [f"package set under {nupkg_dir} does not match {ids_file}"]
    if missing:
        lines.append("  missing: " + ", ".join(missing))
    if extra:
        lines.append("  extra:   " + ", ".join(extra))
    fail("\n".join(lines))

def published_dependencies_by_id(deps_by_id, published_ids):
    return {
        package_id: {dep for dep in deps_by_id[package_id] if dep in published_ids and dep != package_id}
        for package_id in published_ids
    }


def alphabetical_tie_break_topological_order(published_ids, published_dependencies_by_id):
    ordered_ids = []
    ordered_set = set()
    while len(ordered_ids) < len(published_ids):
        ready_ids = sorted(
            package_id
            for package_id in published_ids
            if package_id not in ordered_set and not (published_dependencies_by_id[package_id] - ordered_set)
        )
        if not ready_ids:
            cyclic_ids = sorted(published_ids - ordered_set)
            fail("dependency cycle among: " + ", ".join(cyclic_ids))
        next_id = ready_ids[0]
        ordered_ids.append(next_id)
        ordered_set.add(next_id)
    return ordered_ids


ordered_ids = alphabetical_tie_break_topological_order(
    expected_set, published_dependencies_by_id(deps_by_id, expected_set)
)
for package_id in ordered_ids:
    print(path_by_id[package_id])
PY
)"

printf '%s\n' "$ordered_paths"

if [ -z "$push_source" ]; then
  exit 0
fi

while IFS= read -r pkg; do
  [ -n "$pkg" ] || continue
  echo "Pushing $pkg"
  if ! dotnet nuget push "$pkg" \
    --source "$push_source" \
    --api-key "$NUGET_API_KEY" \
    --skip-duplicate; then
    echo "order-nuget-push.sh: push failed for $pkg — stopping; every package pushed" \
      "before this one is a dependency-complete prefix" >&2
    exit 1
  fi
done <<<"$ordered_paths"
