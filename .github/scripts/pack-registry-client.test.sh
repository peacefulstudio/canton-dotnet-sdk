#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/pack-registry-client.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

mkdir -p "${work}/bin" "${work}/repo/src/Client"
touch "${work}/repo/src/Client/Client.csproj"
cat > "${work}/bin/dotnet" <<'STUB'
#!/usr/bin/env bash
echo "dotnet $*" >> "${STUB_LOG}"
[ "$1" = "pack" ] || exit 0
out=""; version=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    -o) out="$2"; shift 2 ;;
    -p:SpliceRegistryClientVersion=*) version="${1#*=}"; shift ;;
    *) shift ;;
  esac
done
touch "${out}/Splice.Api.Token.Registry.Client.${version}.nupkg"
STUB
chmod +x "${work}/bin/dotnet"

failures=0

make_nupkg() {
  local id="$1" version="$2" staging="${work}/staging"
  rm -rf "${staging}" && mkdir -p "${staging}"
  printf '<package><metadata><id>%s</id><version>%s</version></metadata></package>' "${id}" "${version}" > "${staging}/${id}.nuspec"
  (cd "${staging}" && zip -q "${work}/dar-nupkgs/${id}.${version}.nupkg" "${id}.nuspec")
}

run_pack() {
  local project="$1"
  : > "${work}/stub.log"
  output="$(cd "${work}/repo" \
    && PATH="${work}/bin:${PATH}" \
      STUB_LOG="${work}/stub.log" \
      CLIENT_PROJECT="${project}" \
      CLIENT_BASE_VERSION=0.10.0 \
      NUPKG_DIR="${work}/dar-nupkgs" \
      LOCAL_FEED="${work}/local-feed" \
      RUNTIME_VERSION=0.8.0 \
      "${script}" 2>&1)"
  status=$?
}

reset_dirs() {
  rm -rf "${work}/dar-nupkgs" "${work}/local-feed" && mkdir -p "${work}/dar-nupkgs" "${work}/local-feed"
}

expect() {
  local name="$1" want_status="$2" want_text="${3:-}"
  if { [ "${want_status}" = "0" ] && [ "${status}" -ne 0 ]; } \
    || { [ "${want_status}" != "0" ] && [ "${status}" -eq 0 ]; } \
    || { [ -n "${want_text}" ] && [[ "${output}" != *"${want_text}"* ]]; }; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

expect_log() {
  if grep -qF -- "$2" "${work}/stub.log"; then echo "ok   $1"; else
    echo "FAIL $1: stub log lacks '$2': $(cat "${work}/stub.log")"; failures=$((failures + 1)); fi
}

expect_no_pack() {
  if grep -q "dotnet pack" "${work}/stub.log"; then
    echo "FAIL $1: dotnet pack ran: $(cat "${work}/stub.log")"; failures=$((failures + 1)); else echo "ok   $1"; fi
}

expect_file() {
  if [ -f "$2" ]; then echo "ok   $1"; else echo "FAIL $1: missing $2"; failures=$((failures + 1)); fi
}

reset_dirs
make_nupkg Splice.Amulet 0.1.17.3
make_nupkg Splice.Wallet 0.1.16.3
run_pack src/Client/Client.csproj
expect "a stable release packs the client and succeeds" 0
expect_log "the client version is the Splice release plus the DAR ordinal" "-p:SpliceRegistryClientVersion=0.10.0.3"
expect_log "the SDK version is passed as Version" "-p:Version=0.8.0"
expect_log "the pack targets the project" "src/Client/Client.csproj"
expect_file "the client nupkg lands beside the DAR packages" "${work}/dar-nupkgs/Splice.Api.Token.Registry.Client.0.10.0.3.nupkg"
expect_file "the client nupkg is added to the local feed" "${work}/local-feed/Splice.Api.Token.Registry.Client.0.10.0.3.nupkg"

reset_dirs
make_nupkg Splice.Amulet 0.1.17.5-preview.4
make_nupkg Splice.Wallet 0.1.16.5-preview.4
run_pack src/Client/Client.csproj
expect "a prerelease run packs the client" 0
expect_log "the prerelease suffix is copied from the DAR packages" "-p:SpliceRegistryClientVersion=0.10.0.5-preview.4"

reset_dirs
make_nupkg Splice.Amulet 0.1.17.3
run_pack src/Missing/Missing.csproj
expect "a project not yet promoted is skipped with a notice" 0 "::notice::"
expect_no_pack "a skipped project never reaches dotnet pack"

reset_dirs
make_nupkg Splice.Amulet 0.1.17.3
run_pack ""
expect "no configured project is a silent no-op" 0
expect_no_pack "no configured project never reaches dotnet pack"

reset_dirs
make_nupkg Splice.Amulet 0.1.17.3
make_nupkg Splice.Wallet 0.1.16.4
run_pack src/Client/Client.csproj
expect "DAR packages on two ordinals fail with an error annotation" 1 "::error::"
expect_no_pack "an ambiguous ordinal never reaches dotnet pack"

reset_dirs
make_nupkg Splice.Amulet 0.1.17.3
make_nupkg Splice.Wallet 0.1.16.3-preview.4
run_pack src/Client/Client.csproj
expect "DAR packages with different prerelease suffixes fail" 1 "::error::"

reset_dirs
run_pack src/Client/Client.csproj
expect "an empty DAR package directory fails" 1 "::error::"
expect_no_pack "an empty DAR package directory never reaches dotnet pack"

if [ "${failures}" -ne 0 ]; then
  echo "${failures} assertion(s) failed"
  exit 1
fi
echo "all assertions passed"
