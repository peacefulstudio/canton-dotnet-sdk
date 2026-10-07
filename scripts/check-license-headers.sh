#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail
export LC_ALL=C

usage() {
  cat <<EOF
Usage: $0 [--fix] [--print-scan-paths] [ROOT]

Assert that every file under SCAN_PATHS whose type can carry a comment opens
with this repository's two-line SPDX header:

  <prefix> Copyright <year> Peaceful Studio OÜ
  <prefix> SPDX-License-Identifier: Apache-2.0

The two lines must be adjacent, in that order, within the first
$HEADER_SEARCH_LINES lines, and must match literally apart from the four-digit
year. Both the holder and the licence identifier are asserted, so a header
naming a different holder, or declaring a different licence, fails. A licence
substring appearing somewhere near the top of a file is not enough: this
repository migrated away from "All rights reserved", and the gate has to be
able to see a regression back to it.

Every file type present under the scan paths must be classified, either in
COMMENT_PREFIX_BY_TYPE (a header is required, written with that comment prefix)
or in TYPES_CARRYING_NO_HEADER (data and markup this repository does not
header). A type in neither list fails the run rather than being skipped, so a
new file type cannot enter the tree unchecked. Files with no extension are read
as scripts: they must start with a shebang and carry the '#' header.

INTERNAL_ONLY_SCAN_PATHS are walked only in a tree carrying .gitpublic, the
internal source tree. This checker ships to the public mirror, which authors its
own .github/scripts, so there it is not scanned.

Each classified comment type must match at least one file. A type that matches
nothing means the check has stopped running, not that it passed.

A .licenseignore beside ROOT, one path prefix per line, drops paths whose
headers are a third party's to maintain. Its absence only ever widens the scan,
so a tree without one is checked more strictly, never less.

Under a git work tree the walk is intersected with the index, so build output
and other ignored trees are out of scope while anything staged for commit is
in. A tree git knows nothing about is walked whole: like a missing
.licenseignore, that only ever widens the scan.

With --fix, insert the header into any file reported as missing one, below a
shebang or an '@echo off' line where one leads the file.
With --print-scan-paths, list the scan paths and exit; list-agreement gates
derive their scope from that output rather than restating it.
ROOT defaults to the repository root; pass one to check a tree elsewhere.

Exit codes: 0 all headers present, 1 at least one header missing or malformed,
2 the check could not be carried out.
EOF
}

SCAN_PATHS=(
  src
  tests
  jvm-helper
  samples
  scripts
  conformance
  proto
  benchmarks
  CONTEXT.md
  Directory.Build.props
  coverage.settings.xml
  .editorconfig
  .pre-commit-config.yaml
)

INTERNAL_ONLY_SCAN_PATHS=(
  .github/scripts
)

COMMENT_PREFIX_BY_TYPE=(
  "cmd:rem"
  "cs://"
  "daml:--"
  "editorconfig:#"
  "properties:#"
  "proto://"
  "sbt://"
  "scala://"
  "sh:#"
  "yaml:#"
)

TYPES_CARRYING_NO_HEADER=(
  binpb
  config
  csproj
  dalf
  dar
  json
  manifest
  md
  patch
  props
  refitter
  sha256
  targets
  txt
  xml
)

HOLDER="Peaceful Studio OÜ"
SPDX_LINE="SPDX-License-Identifier: Apache-2.0"
HEADER_SEARCH_LINES=10
IGNORE_FILE=".licenseignore"
SCRIPT_TYPE="shebang script"

FIX=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    -h | --help)
      usage
      exit 0
      ;;
    --print-scan-paths)
      printf '%s\n' "${SCAN_PATHS[@]}" "${INTERNAL_ONLY_SCAN_PATHS[@]}"
      exit 0
      ;;
    --fix)
      FIX="1"
      shift
      ;;
    -*)
      echo "check-license-headers.sh: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
    *)
      break
      ;;
  esac
done

if [ "$#" -gt 1 ]; then
  echo "check-license-headers.sh: unexpected extra arguments: ${*:2}" >&2
  exit 2
fi

root="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
if [ ! -d "$root" ]; then
  echo "check-license-headers.sh: root is not a directory: $root" >&2
  exit 2
fi
cd "$root"

if [ -f .gitpublic ]; then
  SCAN_PATHS+=("${INTERNAL_ONLY_SCAN_PATHS[@]}")
fi

for path in "${SCAN_PATHS[@]}"; do
  if [ ! -e "$path" ]; then
    echo "check-license-headers.sh: scan path is missing from the working tree: $path" >&2
    echo "A scan path that does not resolve would silently drop that part of the tree" >&2
    echo "from the run and still report success." >&2
    exit 2
  fi
done

ignored_prefixes=()
if [ -f "$IGNORE_FILE" ]; then
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%%#*}"
    line="${line#"${line%%[![:space:]]*}"}"
    line="${line%"${line##*[![:space:]]}"}"
    [ -n "$line" ] || continue
    ignored_prefixes+=("${line%/}")
  done <"$IGNORE_FILE"
fi

is_ignored() {
  local file="$1" prefix
  for prefix in ${ignored_prefixes+"${ignored_prefixes[@]}"}; do
    case "$file" in
      "$prefix" | "$prefix"/*) return 0 ;;
    esac
  done
  return 1
}

prefix_for_type() {
  local wanted="$1" entry
  for entry in "${COMMENT_PREFIX_BY_TYPE[@]}"; do
    if [ "${entry%%:*}" = "$wanted" ]; then
      printf '%s' "${entry#*:}"
      return 0
    fi
  done
  return 1
}

type_carries_no_header() {
  local wanted="$1" entry
  for entry in "${TYPES_CARRYING_NO_HEADER[@]}"; do
    if [ "$entry" = "$wanted" ]; then
      return 0
    fi
  done
  return 1
}

header_matches() {
  local file="$1" prefix="$2"
  head -n "$HEADER_SEARCH_LINES" "$file" \
    | awk -v prefix="$prefix" -v holder="$HOLDER" -v spdx="$SPDX_LINE" '
      { sub(/\r$/, ""); line[NR] = $0 }
      END {
        copyright = "^" prefix " Copyright [0-9][0-9][0-9][0-9] " holder "$"
        identifier = prefix " " spdx
        for (i = 1; i < NR; i++) {
          if (line[i] ~ copyright && line[i + 1] == identifier) {
            exit 0
          }
        }
        exit 1
      }
    '
}

first_line() {
  head -n 1 "$1" | tr -d '\r'
}

leading_lines_to_preserve() {
  case "$(first_line "$1")" in
    '#!'* | '@echo off' | '@ECHO OFF') printf '1' ;;
    *) printf '0' ;;
  esac
}

uses_carriage_returns() {
  local first
  first="$(head -n 1 "$1")"
  case "$first" in
    *$'\r') return 0 ;;
  esac
  return 1
}

insert_header() {
  local file="$1" prefix="$2" preserved terminator staged
  preserved="$(leading_lines_to_preserve "$file")"
  terminator=$'\n'
  if uses_carriage_returns "$file"; then
    terminator=$'\r\n'
  fi
  staged="$(mktemp "${TMPDIR:-/tmp}/license-header.XXXXXX")"
  trap "rm -f -- $(printf '%q' "$staged")" EXIT INT TERM
  {
    if [ "$preserved" -gt 0 ]; then
      head -n "$preserved" "$file"
    fi
    printf -- '%s Copyright %s %s%s' "$prefix" "$(date +%Y)" "$HOLDER" "$terminator"
    printf -- '%s %s%s' "$prefix" "$SPDX_LINE" "$terminator"
    tail -n "+$((preserved + 1))" "$file"
  } >"$staged"
  cat -- "$staged" >"$file"
  rm -f -- "$staged"
  trap - EXIT INT TERM
}

walked_files() {
  find "${SCAN_PATHS[@]}" \
    \( -type d \( -name bin -o -name obj -o -name target -o -name .daml \
    -o -name .git -o -name Snapshots -o -name Generated \) -prune \) -o \
    \( -type f -print \) \
    | sort
}

files_tracked_by_git() {
  git rev-parse --is-inside-work-tree >/dev/null 2>&1 || return 0
  git -c core.quotePath=false ls-files --cached -- "${SCAN_PATHS[@]}" | sort
}

scan_files() {
  local walked tracked
  walked="$(walked_files)" || return 1
  tracked="$(files_tracked_by_git)" || return 1
  if [ -z "$tracked" ]; then
    printf '%s' "$walked"
    return 0
  fi
  comm -12 <(printf '%s\n' "$walked") <(printf '%s\n' "$tracked")
}

declare -A files_checked_by_type=()
unclassified_types=()
data_files_without_extension=()
missing_header=0
checked_total=0

assert_header() {
  local file="$1" prefix="$2" type="$3"
  files_checked_by_type["$type"]=$((${files_checked_by_type["$type"]:-0} + 1))
  checked_total=$((checked_total + 1))
  if header_matches "$file" "$prefix"; then
    return 0
  fi
  if [ -n "$FIX" ]; then
    insert_header "$file" "$prefix"
    return 0
  fi
  echo "$file"
  missing_header=1
}

listing="$(scan_files)" || {
  echo "check-license-headers.sh: could not enumerate the scan paths (see above)." >&2
  echo "Part of the tree was unreadable, so those files were never checked." >&2
  exit 2
}

while IFS= read -r file; do
  [ -n "$file" ] || continue
  if is_ignored "$file"; then
    continue
  fi
  base="${file##*/}"
  if [ "${base%.*}" = "$base" ]; then
    if [ "$(head -c 2 -- "$file")" = '#!' ]; then
      assert_header "$file" '#' "$SCRIPT_TYPE"
    else
      data_files_without_extension+=("$file")
    fi
    continue
  fi
  extension="${base##*.}"
  if prefix="$(prefix_for_type "$extension")"; then
    assert_header "$file" "$prefix" "$extension"
  elif type_carries_no_header "$extension"; then
    continue
  else
    unclassified_types+=("$extension  $file")
  fi
done <<<"$listing"

if [ "${#unclassified_types[@]}" -gt 0 ]; then
  echo "check-license-headers.sh: unclassified file type(s) under the scan paths:" >&2
  printf '  %s\n' "${unclassified_types[@]}" >&2
  echo "Add each type to COMMENT_PREFIX_BY_TYPE with its comment prefix, or to" >&2
  echo "TYPES_CARRYING_NO_HEADER. An unclassified type would otherwise go unchecked." >&2
  exit 2
fi

if [ "${#data_files_without_extension[@]}" -gt 0 ]; then
  echo "check-license-headers.sh: file(s) without an extension and without a shebang:" >&2
  printf '  %s\n' "${data_files_without_extension[@]}" >&2
  echo "The gate reads an extensionless file as a script and cannot classify these." >&2
  echo "Give each an extension, or list it in $IGNORE_FILE when its header is a third" >&2
  echo "party's to maintain." >&2
  exit 2
fi

for entry in "${COMMENT_PREFIX_BY_TYPE[@]}" "$SCRIPT_TYPE:#"; do
  classified_type="${entry%%:*}"
  if [ "${files_checked_by_type["$classified_type"]:-0}" -eq 0 ]; then
    echo "check-license-headers.sh: no $classified_type files matched under the scan paths." >&2
    echo "A classified type that matches nothing means the check is not running rather" >&2
    echo "than that it passed. Remove the type or fix the scan paths." >&2
    exit 2
  fi
done

echo "checked $checked_total file(s) across ${#files_checked_by_type[@]} comment type(s)" >&2

exit "$missing_header"
