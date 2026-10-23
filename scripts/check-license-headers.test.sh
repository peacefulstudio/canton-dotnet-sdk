#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

usage() {
  cat <<EOF
Usage: $0

Regression test for scripts/check-license-headers.sh. Builds throwaway trees
and asserts the gate's exit code, covering the verdicts the gate exists to
deliver and, above all, the silent-pass shapes a licence-substring test lets
through:

  1. every header present — exit 0;
  2. a .cs header naming another copyright holder — exit 1;
  3. a .cs header declaring another licence — exit 1;
  4. a .cs header whose SPDX line is gone but whose copyright line remains —
     exit 1;
  5. a copyright and an SPDX line separated by a blank line — exit 1, the two
     have to be adjacent;
  6. a .daml SPDX line reading Apache-2X0 — exit 1, the match is literal and
     not a regex whose . stands for any character;
  7. .sbt, .properties and .cmd headers stripped — exit 1 each, these are the
     types a comment-style table has to carry because no general tool knows
     them;
  8. a file type classified nowhere — exit 2;
  9. a file with neither extension nor shebang — exit 2;
 10. a classified comment type that matches no file — exit 2;
 11. a scan path missing from the tree — exit 2, including an internal-only
     scan path in a tree carrying .gitpublic;
 11a. a public-mirror tree (no .gitpublic) with a headerless mirror-authored
     .github/scripts file — exit 0, and exit 1 once .gitpublic is put back, so
     the pass is the skip list's doing;
 12. a third-party header under a .licenseignore path — exit 0, and exit 1
     once the ignore file is taken away, so the pass is the ignore file's
     doing and not the gate going quiet;
 13. a git-ignored build artifact under a git work tree — exit 0, and exit 2
     once that same file is force-added to the index, so the pass is the
     index's doing and not the gate going quiet;
 14. --fix writes the headers, keeps a shebang and an '@echo off' first, keeps
     the executable bit, preserves CRLF in files that use it, leaves no
     staging file behind, and the tree then passes a plain check;
 15. with --root and PATH arguments, only the passed files are checked:
     a headerless one fails — exit 1, a good one passes without the
     type-coverage self-check, and a file outside the scan paths, under a
     .licenseignore path, under a pruned directory, or no longer on disk is
     skipped; an in-scope file of an unclassified type or without extension
     or shebang is still reported — exit 2; --fix touches only the passed
     files;
 16. with no arguments the gate scans the tree it lives in, as it always has.

Exits non-zero on any assertion failure.
EOF
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gate="$script_dir/check-license-headers.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

HOLDER="Peaceful Studio OÜ"
SPDX_LINE="SPDX-License-Identifier: Apache-2.0"

headered() {
  local prefix="$1" body="$2"
  printf '%s Copyright 2026 %s\n%s %s\n\n%s\n' "$prefix" "$HOLDER" "$prefix" "$SPDX_LINE" "$body"
}

build_tree() {
  local root="$1"
  mkdir -p "$root/src" "$root/tests" "$root/jvm-helper/project" "$root/samples" \
    "$root/scripts" "$root/conformance" "$root/proto" "$root/benchmarks" \
    "$root/.github/scripts"

  headered '//' 'namespace Fixture { public sealed class Thing { } }' >"$root/src/Thing.cs"
  headered '//' 'class ThingSpec' >"$root/tests/ThingSpec.scala"
  headered '//' 'name := "fixture"' >"$root/jvm-helper/build.sbt"
  headered '//' 'syntax = "proto3";' >"$root/proto/fixture.proto"
  headered '#' 'sbt.version=1.10.5' >"$root/jvm-helper/project/build.properties"
  headered '--' 'module Fixture where' >"$root/conformance/Fixture.daml"
  headered '#' 'sdk-version: 0.0.0' >"$root/conformance/daml.yaml"
  headered '#' 'echo fixture' >"$root/samples/run.sh"
  headered '//' 'namespace Fixture { public sealed class Bench { } }' >"$root/benchmarks/Bench.cs"
  headered '#' 'echo fixture' >"$root/.github/scripts/ci.sh"
  headered '#' 'repos: []' >"$root/.pre-commit-config.yaml"
  headered '#' 'root = true' >"$root/.editorconfig"

  printf '@echo off\nrem Copyright 2026 %s\nrem %s\necho fixture\n' \
    "$HOLDER" "$SPDX_LINE" >"$root/scripts/entrypoint.cmd"
  printf '#!/usr/bin/env bash\n# Copyright 2026 %s\n# %s\necho fixture\n' \
    "$HOLDER" "$SPDX_LINE" >"$root/scripts/entrypoint"
  chmod +x "$root/scripts/entrypoint"

  printf '# Fixture\n' >"$root/CONTEXT.md"
  printf '/src/\n' >"$root/.gitpublic"
  printf '<Project />\n' >"$root/Directory.Build.props"
  printf '<Configuration />\n' >"$root/coverage.settings.xml"
}

drop_lines() {
  local file="$1" first="$2" last="$3"
  awk -v first="$first" -v last="$last" 'NR < first || NR > last' "$file" >"$file.reduced"
  cat "$file.reduced" >"$file"
  rm -f "$file.reduced"
}

replace_in_file() {
  local file="$1" pattern="$2" replacement="$3"
  sed "s/$pattern/$replacement/" "$file" >"$file.replaced"
  cat "$file.replaced" >"$file"
  rm -f "$file.replaced"
}

failures=0

report() {
  local outcome="$1" label="$2"
  if [ "$outcome" = "pass" ]; then
    echo "PASS: $label"
  else
    echo "FAIL: $label"
    failures=$((failures + 1))
  fi
}

assert_exit() {
  local label="$1" expected="$2" tree="$3" flag="${4:-}"
  local actual=0
  if [ -n "$flag" ]; then
    bash "$gate" "$flag" --root "$tree" >/dev/null 2>&1 || actual=$?
  else
    bash "$gate" --root "$tree" >/dev/null 2>&1 || actual=$?
  fi
  if [ "$actual" -eq "$expected" ]; then
    report pass "$label"
  else
    report fail "$label — expected exit $expected, got $actual"
  fi
}

assert_exit_for_paths() {
  local label="$1" expected="$2" tree="$3" actual=0
  shift 3
  bash "$gate" --root "$tree" "$@" >/dev/null 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    report pass "$label"
  else
    report fail "$label — expected exit $expected, got $actual"
  fi
}

new_tree() {
  local tree="$work_dir/$1"
  build_tree "$tree"
  printf '%s' "$tree"
}

complete_tree="$(new_tree complete)"
assert_exit "a tree with every header present passes" 0 "$complete_tree"

foreign_holder_tree="$(new_tree foreign-holder)"
replace_in_file "$foreign_holder_tree/src/Thing.cs" \
  "Copyright 2026 .*" "Copyright 1999 Some Other Corp. All rights reserved."
assert_exit "a .cs header naming another copyright holder fails the gate" 1 "$foreign_holder_tree"

foreign_licence_tree="$(new_tree foreign-licence)"
replace_in_file "$foreign_licence_tree/src/Thing.cs" "Apache-2\.0" "GPL-3.0"
assert_exit "a .cs header declaring another licence fails the gate" 1 "$foreign_licence_tree"

no_spdx_tree="$(new_tree no-spdx)"
drop_lines "$no_spdx_tree/src/Thing.cs" 2 2
assert_exit "a .cs header that lost only its SPDX line fails the gate" 1 "$no_spdx_tree"

split_header_tree="$(new_tree split-header)"
awk 'NR == 1 { print; print ""; next } { print }' "$split_header_tree/src/Thing.cs" \
  >"$split_header_tree/src/Thing.split"
cat "$split_header_tree/src/Thing.split" >"$split_header_tree/src/Thing.cs"
rm -f "$split_header_tree/src/Thing.split"
assert_exit "a copyright and SPDX line no longer adjacent fails the gate" 1 "$split_header_tree"

mutated_daml_tree="$(new_tree mutated-daml)"
replace_in_file "$mutated_daml_tree/conformance/Fixture.daml" "Apache-2\.0" "Apache-2X0"
assert_exit "a .daml SPDX line reading Apache-2X0 fails the gate" 1 "$mutated_daml_tree"

for stripped in sbt properties cmd; do
  tree="$(new_tree "headerless-$stripped")"
  case "$stripped" in
    sbt) drop_lines "$tree/jvm-helper/build.sbt" 1 2 ;;
    properties) drop_lines "$tree/jvm-helper/project/build.properties" 1 2 ;;
    cmd) drop_lines "$tree/scripts/entrypoint.cmd" 2 3 ;;
  esac
  assert_exit "a headerless .$stripped no general tool knows fails the gate" 1 "$tree"
done

unclassified_tree="$(new_tree unclassified)"
printf 'body { color: red }\n' >"$unclassified_tree/src/theme.css"
assert_exit "a file type classified nowhere is reported, not skipped" 2 "$unclassified_tree"

opaque_tree="$(new_tree opaque)"
printf '3.4.11\n' >"$opaque_tree/src/version"
assert_exit "a file with neither extension nor shebang is reported" 2 "$opaque_tree"

no_daml_tree="$(new_tree no-daml)"
rm "$no_daml_tree/conformance/Fixture.daml"
assert_exit "a classified comment type that matches nothing is reported" 2 "$no_daml_tree"

missing_path_tree="$(new_tree missing-path)"
rm -rf "$missing_path_tree/conformance"
assert_exit "a scan path missing from the tree is reported" 2 "$missing_path_tree"

for internal_path in .github/scripts; do
  missing_internal_tree="$(new_tree "missing-unpromoted-${internal_path//\//-}")"
  rm -rf "${missing_internal_tree:?}/$internal_path"
  assert_exit "an internal-only scan path missing beside .gitpublic is reported" 2 "$missing_internal_tree"
done

public_tree="$(new_tree public-mirror)"
printf '#!/usr/bin/env bash\necho mirror-authored\n' >"$public_tree/.github/scripts/leak-check.sh"
rm -f "$public_tree/.gitpublic"
assert_exit "a public tree without .gitpublic skips the internal-only scan paths" 0 "$public_tree"
printf '/src/\n' >"$public_tree/.gitpublic"
assert_exit "the same tree beside .gitpublic is reported, so the pass was the skip list's doing" 1 "$public_tree"

vendored_tree="$(new_tree vendored)"
mkdir -p "$vendored_tree/src/vendor"
printf '// Copyright (c) 2025 Another Vendor. All rights reserved.\n// %s\n\nsyntax = "proto3";\n' \
  "$SPDX_LINE" >"$vendored_tree/src/vendor/foreign.proto"
assert_exit "a third-party header with no ignore file fails the gate" 1 "$vendored_tree"
printf 'src/vendor/\n' >"$vendored_tree/.licenseignore"
assert_exit "a third-party header under a .licenseignore path passes" 0 "$vendored_tree"

ignored_artifact_tree="$(new_tree ignored-artifact)"
git -C "$ignored_artifact_tree" init --quiet >/dev/null 2>&1
printf 'src/build-output/\n' >"$ignored_artifact_tree/.gitignore"
mkdir -p "$ignored_artifact_tree/src/build-output"
printf 'not source\n' >"$ignored_artifact_tree/src/build-output/Vendor.dll"
git -C "$ignored_artifact_tree" add --all >/dev/null 2>&1
assert_exit "a git-ignored build artifact is out of scope under a git work tree" 0 "$ignored_artifact_tree"
git -C "$ignored_artifact_tree" add --force src/build-output/Vendor.dll >/dev/null 2>&1
assert_exit "the same artifact once tracked is reported, so the pass was the index's doing" 2 "$ignored_artifact_tree"

fix_tree="$(new_tree fix)"
drop_lines "$fix_tree/conformance/Fixture.daml" 1 2
drop_lines "$fix_tree/jvm-helper/build.sbt" 1 2
drop_lines "$fix_tree/jvm-helper/project/build.properties" 1 2
drop_lines "$fix_tree/scripts/entrypoint.cmd" 2 3
drop_lines "$fix_tree/scripts/entrypoint" 2 3
assert_exit "--fix writes the missing headers" 0 "$fix_tree" --fix
assert_exit "the tree --fix wrote then passes a plain check" 0 "$fix_tree"

if [ "$(head -n 1 "$fix_tree/scripts/entrypoint")" = "#!/usr/bin/env bash" ]; then
  report pass "--fix keeps the shebang on the first line"
else
  report fail "--fix moved the shebang off the first line"
fi

if [ "$(head -n 1 "$fix_tree/scripts/entrypoint.cmd")" = "@echo off" ]; then
  report pass "--fix keeps '@echo off' on the first line"
else
  report fail "--fix moved '@echo off' off the first line"
fi

if [ -x "$fix_tree/scripts/entrypoint" ]; then
  report pass "--fix keeps the executable bit"
else
  report fail "--fix dropped the executable bit"
fi

# Run --fix on a fresh stripped tree with TMPDIR redirected inside the tree so we
# can verify that staging files land in a known location and are cleaned up.
staging_check_tree="$(new_tree staging-check)"
drop_lines "$staging_check_tree/conformance/Fixture.daml" 1 2
mkdir -p "$staging_check_tree/.tmpdir"
TMPDIR="$staging_check_tree/.tmpdir" bash "$gate" --fix --root "$staging_check_tree" >/dev/null 2>&1
staging_leftovers="$(find "$staging_check_tree/.tmpdir" -name 'license-header.*')"
if [ -z "$staging_leftovers" ]; then
  report pass "--fix leaves no staging file behind"
else
  report fail "--fix left staging files behind: $staging_leftovers"
fi

# Verify CRLF preservation: create a .cmd with CRLF line endings (no header),
# run --fix, and assert the file still uses CRLF after the header is inserted.
crlf_tree="$(new_tree crlf)"
printf '@echo off\r\necho fixture\r\n' >"$crlf_tree/scripts/entrypoint.cmd"
assert_exit "--fix on a CRLF .cmd exits 0" 0 "$crlf_tree" --fix
if grep -q $'\r' "$crlf_tree/scripts/entrypoint.cmd"; then
  report pass "--fix preserves CRLF in .cmd files"
else
  report fail "--fix dropped CRLF line endings in a .cmd file"
fi

for unheadered in benchmarks/Bench.cs .github/scripts/ci.sh; do
  unheadered_tree="$(new_tree "unheadered-${unheadered//\//-}")"
  drop_lines "$unheadered_tree/$unheadered" 1 2
  assert_exit "a headerless $unheadered fails the gate" 1 "$unheadered_tree"
done

path_headerless_tree="$(new_tree path-headerless)"
drop_lines "$path_headerless_tree/src/Thing.cs" 1 2
assert_exit_for_paths "a passed file missing its header fails the gate" 1 \
  "$path_headerless_tree" src/Thing.cs

path_good_tree="$(new_tree path-good)"
rm "$path_good_tree/conformance/Fixture.daml"
assert_exit_for_paths "a passed file with a good header passes without the type-coverage self-check" 0 \
  "$path_good_tree" src/Thing.cs


path_outside_tree="$(new_tree path-outside)"
mkdir -p "$path_outside_tree/docs"
printf 'namespace Notes { }\n' >"$path_outside_tree/docs/Notes.cs"
assert_exit_for_paths "a passed file outside the scan paths is ignored" 0 \
  "$path_outside_tree" docs/Notes.cs

path_untouched_tree="$(new_tree path-untouched)"
drop_lines "$path_untouched_tree/src/Thing.cs" 1 2
assert_exit_for_paths "a headerless file that was not passed is not looked at" 0 \
  "$path_untouched_tree" benchmarks/Bench.cs

path_ignored_tree="$(new_tree path-ignored)"
mkdir -p "$path_ignored_tree/src/vendor"
printf 'syntax = "proto3";\n' >"$path_ignored_tree/src/vendor/foreign.proto"
printf 'src/vendor/\n' >"$path_ignored_tree/.licenseignore"
assert_exit_for_paths "a passed file under a .licenseignore path is skipped" 0 \
  "$path_ignored_tree" src/vendor/foreign.proto

path_generated_tree="$(new_tree path-generated)"
mkdir -p "$path_generated_tree/src/Generated"
printf 'namespace Emitted { }\n' >"$path_generated_tree/src/Generated/Emitted.cs"
assert_exit_for_paths "a passed file under a pruned directory is skipped" 0 \
  "$path_generated_tree" src/Generated/Emitted.cs

path_deleted_tree="$(new_tree path-deleted)"
assert_exit_for_paths "a passed path that no longer exists is skipped" 0 \
  "$path_deleted_tree" src/Gone.cs

path_unclassified_tree="$(new_tree path-unclassified)"
printf 'body { color: red }\n' >"$path_unclassified_tree/src/theme.css"
assert_exit_for_paths "a passed in-scope file of a type classified nowhere is reported" 2 \
  "$path_unclassified_tree" src/theme.css

path_opaque_tree="$(new_tree path-opaque)"
printf '3.4.11\n' >"$path_opaque_tree/src/version"
assert_exit_for_paths "a passed in-scope file with neither extension nor shebang is reported" 2 \
  "$path_opaque_tree" src/version

path_absolute_tree="$(new_tree path-absolute)"
drop_lines "$path_absolute_tree/src/Thing.cs" 1 2
assert_exit_for_paths "a passed absolute path inside the root is checked" 1 \
  "$path_absolute_tree" "$path_absolute_tree/src/Thing.cs"
assert_exit_for_paths "a passed absolute path outside the root is ignored" 0 \
  "$path_absolute_tree" "$work_dir/elsewhere/Thing.cs"

path_dot_slash_tree="$(new_tree path-dot-slash)"
drop_lines "$path_dot_slash_tree/src/Thing.cs" 1 2
assert_exit_for_paths "a passed ./-prefixed path is checked" 1 \
  "$path_dot_slash_tree" ./src/Thing.cs

path_mixed_tree="$(new_tree path-mixed)"
drop_lines "$path_mixed_tree/src/Thing.cs" 1 2
assert_exit_for_paths "one headerless file among several passed ones fails the gate" 1 \
  "$path_mixed_tree" benchmarks/Bench.cs src/Thing.cs docs/Notes.cs

path_fix_tree="$(new_tree path-fix)"
drop_lines "$path_fix_tree/src/Thing.cs" 1 2
drop_lines "$path_fix_tree/benchmarks/Bench.cs" 1 2
assert_exit_for_paths "--fix with a passed path exits 0" 0 "$path_fix_tree" --fix src/Thing.cs
if grep -q 'SPDX-License-Identifier' "$path_fix_tree/src/Thing.cs" \
  && ! grep -q 'SPDX-License-Identifier' "$path_fix_tree/benchmarks/Bench.cs"; then
  report pass "--fix with a passed path writes that file's header and no other"
else
  report fail "--fix with a passed path touched the wrong files"
fi

default_root_tree="$(new_tree default-root)"
cp "$gate" "$default_root_tree/scripts/check-license-headers.sh"
default_root_exit=0
bash "$default_root_tree/scripts/check-license-headers.sh" >/dev/null 2>&1 || default_root_exit=$?
if [ "$default_root_exit" -eq 0 ]; then
  report pass "with no arguments the gate scans the tree it lives in"
else
  report fail "with no arguments the gate exited $default_root_exit on a complete tree"
fi
drop_lines "$default_root_tree/benchmarks/Bench.cs" 1 2
default_root_exit=0
bash "$default_root_tree/scripts/check-license-headers.sh" >/dev/null 2>&1 || default_root_exit=$?
if [ "$default_root_exit" -eq 1 ]; then
  report pass "with no arguments the gate reports a headerless file anywhere in the tree"
else
  report fail "with no arguments the gate exited $default_root_exit on a tree with a headerless file"
fi

if [ "$failures" -ne 0 ]; then
  echo "$failures assertion(s) failed."
  exit 1
fi

echo "All assertions passed."
