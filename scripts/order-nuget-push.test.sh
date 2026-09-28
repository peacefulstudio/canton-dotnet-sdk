#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

usage() {
  cat <<EOF
Usage: $0

Regression test for scripts/order-nuget-push.sh. Builds throwaway nupkg
fixtures (tiny zips holding a nuspec) that mirror the real ledger fold-in
dependency graph across all 16 published packages, then asserts:

  1. the full 16-package set sorts into the exact dependency-first order
     pinned below, with Canton.Ledger.Grpc ordered before Daml.Runtime.Grpc
     (the issue body had that edge backwards — this fixture and its pinned
     order catch a regression back to it), and an external (non-published)
     dependency is ignored rather than rejected or reordered;
  2. a missing published id fails with exit 1, naming it;
  3. an unpublished extra id fails with exit 1, naming it;
  4. a dependency cycle among the packed set fails with exit 1;
  5. --push runs 'dotnet nuget push --skip-duplicate' through a stub, in the
     pinned order, with the right --source/--api-key flags;
  6. --push stops at the first failing package and pushes no further one;
  7. --push without NUGET_API_KEY set fails closed (exit 2), never reaching
     the (stub) push command.

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
gate="$script_dir/order-nuget-push.sh"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

PINNED_DEPENDENCY_FIRST_ALPHABETICAL_TIE_BREAK_ORDER=(
  Canton.Ledger.Grpc
  Canton.Ledger.Rest
  Daml.Codegen.Intermediate
  Daml.Runtime
  Daml.Ledger.Abstractions
  Canton.Ledger.Abstractions
  Canton.Ledger.Kernel
  Canton.Ledger.OpenTelemetry
  Canton.Ledger.Pqs.Client
  Canton.Ledger.Rest.Client
  Canton.Ledger.Testing
  Daml.Codegen.CSharp
  Daml.Codegen.Testing.Conformance
  Daml.Ledger.Abstractions.Testing.Conformance
  Daml.Runtime.Grpc
  Canton.Ledger.Grpc.Client
)

write_nupkg() {
  local dir="$1" id="$2"
  shift 2
  PKG_DIR="$dir" PKG_ID="$id" PKG_DEPS="$(printf '%s\n' "$@")" python3 - <<'PY'
import os
import zipfile

pkg_dir = os.environ["PKG_DIR"]
pkg_id = os.environ["PKG_ID"]
deps = [d for d in os.environ.get("PKG_DEPS", "").splitlines() if d]

dependency_xml = "".join(
    f'<dependency id="{dep}" version="0.6.0-preview.1" exclude="Build,Analyzers" />'
    for dep in deps
)
nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{pkg_id}</id>
    <version>0.6.0-preview.1</version>
    <dependencies>
      <group targetFramework="net8.0">
        {dependency_xml}
      </group>
    </dependencies>
  </metadata>
</package>
"""

os.makedirs(pkg_dir, exist_ok=True)
path = os.path.join(pkg_dir, f"{pkg_id}.0.6.0-preview.1.nupkg")
with zipfile.ZipFile(path, "w") as archive:
    archive.writestr(f"{pkg_id}.nuspec", nuspec)
    archive.writestr("lib/net8.0/placeholder.dll", b"")
PY
}

build_full_published_package_set() {
  local dir="$1"
  write_nupkg "$dir" Daml.Runtime Some.External.Package
  write_nupkg "$dir" Daml.Codegen.Intermediate
  write_nupkg "$dir" Canton.Ledger.Grpc
  write_nupkg "$dir" Canton.Ledger.Rest
  write_nupkg "$dir" Daml.Ledger.Abstractions Daml.Runtime
  write_nupkg "$dir" Canton.Ledger.Abstractions Daml.Ledger.Abstractions Daml.Runtime
  write_nupkg "$dir" Canton.Ledger.Kernel Daml.Runtime Canton.Ledger.Abstractions
  write_nupkg "$dir" Canton.Ledger.OpenTelemetry Canton.Ledger.Kernel
  write_nupkg "$dir" Canton.Ledger.Pqs.Client Daml.Runtime Canton.Ledger.Abstractions \
    Canton.Ledger.Kernel
  write_nupkg "$dir" Canton.Ledger.Rest.Client Daml.Ledger.Abstractions Daml.Runtime \
    Canton.Ledger.Abstractions Canton.Ledger.Kernel Canton.Ledger.Rest
  write_nupkg "$dir" Canton.Ledger.Testing Daml.Ledger.Abstractions Daml.Runtime \
    Canton.Ledger.Abstractions
  write_nupkg "$dir" Daml.Codegen.CSharp Daml.Codegen.Intermediate Daml.Runtime \
    Daml.Ledger.Abstractions
  write_nupkg "$dir" Daml.Codegen.Testing.Conformance Daml.Runtime Daml.Ledger.Abstractions
  write_nupkg "$dir" Daml.Ledger.Abstractions.Testing.Conformance Daml.Ledger.Abstractions
  write_nupkg "$dir" Daml.Runtime.Grpc Daml.Runtime Canton.Ledger.Grpc
  write_nupkg "$dir" Canton.Ledger.Grpc.Client Daml.Ledger.Abstractions Daml.Runtime \
    Canton.Ledger.Abstractions Canton.Ledger.Kernel Canton.Ledger.Grpc Daml.Runtime.Grpc
}

failures=0

pass() { echo "PASS: $1"; }
fail_case() {
  echo "FAIL: $1"
  shift
  for line in "$@"; do
    echo "  $line"
  done
  failures=$((failures + 1))
}

test_full_valid_set_sorts_into_pinned_order_with_external_dep_ignored() {
  local valid_dir="$work_dir/valid"
  local actual_order_output valid_exit expected_order_output id
  build_full_published_package_set "$valid_dir"

  actual_order_output="$(bash "$gate" "$valid_dir" 2>"$work_dir/valid.stderr")"
  valid_exit=$?
  expected_order_output=""
  for id in "${PINNED_DEPENDENCY_FIRST_ALPHABETICAL_TIE_BREAK_ORDER[@]}"; do
    expected_order_output+="$valid_dir/$id.0.6.0-preview.1.nupkg"$'\n'
  done
  expected_order_output="${expected_order_output%$'\n'}"

  if [ "$valid_exit" -ne 0 ]; then
    fail_case "the full valid set sorts (exit 0)" "exit was $valid_exit" \
      "stderr: $(cat "$work_dir/valid.stderr")"
  elif [ "$actual_order_output" != "$expected_order_output" ]; then
    fail_case "the full valid set sorts into the pinned order" \
      "expected:" "$expected_order_output" "actual:" "$actual_order_output"
  else
    pass "the full valid set sorts into the pinned order, external dep ignored"
  fi
}

test_canton_ledger_grpc_ordered_before_daml_runtime_grpc() {
  local grpc_line runtime_grpc_line
  grpc_line="$(printf '%s\n' "${PINNED_DEPENDENCY_FIRST_ALPHABETICAL_TIE_BREAK_ORDER[@]}" | grep -n '^Canton.Ledger.Grpc$' | cut -d: -f1)"
  runtime_grpc_line="$(printf '%s\n' "${PINNED_DEPENDENCY_FIRST_ALPHABETICAL_TIE_BREAK_ORDER[@]}" | grep -n '^Daml.Runtime.Grpc$' | cut -d: -f1)"
  if [ "$grpc_line" -lt "$runtime_grpc_line" ]; then
    pass "Canton.Ledger.Grpc is ordered before Daml.Runtime.Grpc"
  else
    fail_case "Canton.Ledger.Grpc is ordered before Daml.Runtime.Grpc" \
      "Canton.Ledger.Grpc at $grpc_line, Daml.Runtime.Grpc at $runtime_grpc_line"
  fi
}

test_missing_published_id_fails_naming_it() {
  local missing_dir="$work_dir/missing" missing_stderr missing_exit
  build_full_published_package_set "$missing_dir"
  rm -f "$missing_dir/Canton.Ledger.Testing.0.6.0-preview.1.nupkg"

  missing_stderr="$(bash "$gate" "$missing_dir" 2>&1 >/dev/null)"
  missing_exit=$?
  if [ "$missing_exit" -eq 1 ] && grep -q "Canton.Ledger.Testing" <<<"$missing_stderr"; then
    pass "a missing published id fails (exit 1), naming it"
  else
    fail_case "a missing published id fails (exit 1), naming it" \
      "exit=$missing_exit" "stderr: $missing_stderr"
  fi
}

test_extra_unpublished_id_fails_naming_it() {
  local extra_dir="$work_dir/extra" extra_stderr extra_exit
  build_full_published_package_set "$extra_dir"
  write_nupkg "$extra_dir" Some.Unpublished.Extra.Package

  extra_stderr="$(bash "$gate" "$extra_dir" 2>&1 >/dev/null)"
  extra_exit=$?
  if [ "$extra_exit" -eq 1 ] && grep -q "Some.Unpublished.Extra.Package" <<<"$extra_stderr"; then
    pass "an extra unpublished id fails (exit 1), naming it"
  else
    fail_case "an extra unpublished id fails (exit 1), naming it" \
      "exit=$extra_exit" "stderr: $extra_stderr"
  fi
}

test_dependency_cycle_fails() {
  local cyclic_dir="$work_dir/cyclic" cyclic_stderr cyclic_exit
  local cycle_root_id="Daml.Runtime" cycle_closing_dependency_id="Canton.Ledger.Grpc.Client"
  build_full_published_package_set "$cyclic_dir"
  write_nupkg "$cyclic_dir" "$cycle_root_id" "$cycle_closing_dependency_id"

  cyclic_stderr="$(bash "$gate" "$cyclic_dir" 2>&1 >/dev/null)"
  cyclic_exit=$?
  if [ "$cyclic_exit" -eq 1 ] && grep -qi "cycle" <<<"$cyclic_stderr"; then
    pass "a dependency cycle fails (exit 1)"
  else
    fail_case "a dependency cycle fails (exit 1)" "exit=$cyclic_exit" "stderr: $cyclic_stderr"
  fi
}

write_stub_dotnet() {
  local bin_dir="$1" log="$2" fail_at_index="${3:-}"
  mkdir -p "$bin_dir"
  cat >"$bin_dir/dotnet" <<EOF
#!/usr/bin/env bash
set -euo pipefail
echo "\$@" >>"$log"
if [ "\${1:-}" = "nuget" ] && [ "\${2:-}" = "push" ]; then
  pushed_pkg="\$3"
  fail_at_index="$fail_at_index"
  if [ -n "\$fail_at_index" ]; then
    count_file="$log.count"
    count=\$(( \$(cat "\$count_file" 2>/dev/null || echo 0) + 1 ))
    echo "\$count" >"\$count_file"
    if [ "\$count" -eq "\$fail_at_index" ]; then
      echo "stub dotnet: simulated push failure for \$pushed_pkg" >&2
      exit 1
    fi
  fi
  exit 0
fi
echo "stub dotnet: unexpected invocation: \$*" >&2
exit 1
EOF
  chmod +x "$bin_dir/dotnet"
}

test_push_runs_stub_in_pinned_order_with_right_flags() {
  local push_dir stub_bin push_log push_stderr push_exit expected_log actual_log id
  push_dir="$work_dir/push"
  build_full_published_package_set "$push_dir"
  stub_bin="$work_dir/stub-bin"
  push_log="$work_dir/push.log"
  : >"$push_log"
  write_stub_dotnet "$stub_bin" "$push_log"

  push_stderr="$(PATH="$stub_bin:$PATH" NUGET_API_KEY="test-key" \
    bash "$gate" "$push_dir" --push "https://example.invalid/v3/index.json" 2>&1 >/dev/null)"
  push_exit=$?

  expected_log=""
  for id in "${PINNED_DEPENDENCY_FIRST_ALPHABETICAL_TIE_BREAK_ORDER[@]}"; do
    expected_log+="nuget push $push_dir/$id.0.6.0-preview.1.nupkg --source https://example.invalid/v3/index.json --api-key test-key --skip-duplicate"$'\n'
  done
  expected_log="${expected_log%$'\n'}"
  actual_log="$(cat "$push_log")"

  if [ "$push_exit" -eq 0 ] && [ "$actual_log" = "$expected_log" ]; then
    pass "--push pushes all 16 through the stub, in the pinned order, with the right flags"
  else
    fail_case "--push pushes all 16 through the stub, in the pinned order, with the right flags" \
      "exit=$push_exit" "stderr: $push_stderr" "expected log:" "$expected_log" "actual log:" "$actual_log"
  fi
}

test_push_stops_at_first_failing_package() {
  local push_fail_dir stub_fail_bin push_fail_log push_fail_exit pushed_count
  push_fail_dir="$work_dir/push-fail"
  build_full_published_package_set "$push_fail_dir"
  stub_fail_bin="$work_dir/stub-fail-bin"
  push_fail_log="$work_dir/push-fail.log"
  : >"$push_fail_log"
  write_stub_dotnet "$stub_fail_bin" "$push_fail_log" 3

  PATH="$stub_fail_bin:$PATH" NUGET_API_KEY="test-key" \
    bash "$gate" "$push_fail_dir" --push "https://example.invalid/v3/index.json" \
    >/dev/null 2>"$work_dir/push-fail.stderr"
  push_fail_exit=$?

  pushed_count="$(grep -c '^nuget push ' "$push_fail_log" || true)"
  if [ "$push_fail_exit" -eq 1 ] && [ "$pushed_count" -eq 3 ]; then
    pass "--push stops at the first failing package (3 attempted, no more)"
  else
    fail_case "--push stops at the first failing package" \
      "exit=$push_fail_exit" "pushed_count=$pushed_count" \
      "stderr: $(cat "$work_dir/push-fail.stderr")"
  fi
}

test_push_without_api_key_fails_closed_never_touching_stub() {
  local no_key_dir stub_no_key_bin no_key_log no_key_exit
  no_key_dir="$work_dir/no-key"
  build_full_published_package_set "$no_key_dir"
  stub_no_key_bin="$work_dir/stub-no-key-bin"
  no_key_log="$work_dir/no-key.log"
  : >"$no_key_log"
  write_stub_dotnet "$stub_no_key_bin" "$no_key_log"

  (
    unset NUGET_API_KEY
    PATH="$stub_no_key_bin:$PATH"
    bash "$gate" "$no_key_dir" --push "https://example.invalid/v3/index.json"
  ) >/dev/null 2>"$work_dir/no-key.stderr"
  no_key_exit=$?

  if [ "$no_key_exit" -eq 2 ] && [ ! -s "$no_key_log" ]; then
    pass "--push without NUGET_API_KEY fails closed (exit 2), stub never invoked"
  else
    fail_case "--push without NUGET_API_KEY fails closed" \
      "exit=$no_key_exit" "log: $(cat "$no_key_log")" "stderr: $(cat "$work_dir/no-key.stderr")"
  fi
}

test_full_valid_set_sorts_into_pinned_order_with_external_dep_ignored
test_canton_ledger_grpc_ordered_before_daml_runtime_grpc
test_missing_published_id_fails_naming_it
test_extra_unpublished_id_fails_naming_it
test_dependency_cycle_fails
test_push_runs_stub_in_pinned_order_with_right_flags
test_push_stops_at_first_failing_package
test_push_without_api_key_fails_closed_never_touching_stub

if [ "$failures" -ne 0 ]; then
  echo "$failures assertion(s) failed."
  exit 1
fi

echo "All assertions passed."
