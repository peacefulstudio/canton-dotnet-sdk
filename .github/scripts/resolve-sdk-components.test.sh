#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/resolve-sdk-components.sh"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

manifest_repo="europe-docker.pkg.dev/da-images/public/sdk-manifests/open-source"
component_repo="europe-docker.pkg.dev/da-images/public/components"

sdk_digest="ade248f090b33ef8a7153689b8a6f4e733074bd1b463f67a29d6be586c91e16f"
damlc_digest="9a25f7f1498ead329fe4dd58e338f34950cff786dbe1f9c106fe4c7df9e7d8b8"
daml_script_digest="c340a6f808e6de2137ea6093e360d83bc3dcf7dc3228ca88b01828d1667c59c1"
other_digest="0000000000000000000000000000000000000000000000000000000000000000"

mkdir -p "${work}/bin" "${work}/registry"
cat > "${work}/bin/oras" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
key="$(echo "$2" | tr '/:@' '___')"
case "$1" in
  pull)
    [ -f "${FAKE_REGISTRY}/${key}" ] || { echo "Error: $2: not found" >&2; exit 1; }
    cp "${FAKE_REGISTRY}/${key}" "$4/$(cat "${FAKE_REGISTRY}/${key}.name")"
    ;;
  resolve)
    [ -f "${FAKE_REGISTRY}/${key}.digest" ] || { echo "Error: $2: not found" >&2; exit 1; }
    cat "${FAKE_REGISTRY}/${key}.digest"
    ;;
esac
EOF
chmod +x "${work}/bin/oras"
export FAKE_REGISTRY="${work}/registry"

failures=0

registry_key() {
  echo "$1" | tr '/:@' '___'
}

publish_manifest() {
  local sdk="$1" digest="$2"
  shift 2
  local key
  key="$(registry_key "${manifest_repo}@sha256:${digest}")"
  {
    echo "apiVersion: digitalasset.com/v1"
    echo "kind: SdkManifest"
    echo "spec:"
    echo "  components:"
    for entry in "$@"; do
      echo "    ${entry%%=*}:"
      echo "      version: ${entry#*=}"
    done
    echo "  assistant:"
    echo "    version: 1.0.22"
    echo "  version: ${sdk}"
    echo "  edition: open-source"
  } > "${FAKE_REGISTRY}/${key}"
  echo "${sdk}.yaml" > "${FAKE_REGISTRY}/${key}.name"
}

publish_component_tag() {
  echo "sha256:$3" > "${FAKE_REGISTRY}/$(registry_key "${component_repo}/$1:$2").digest"
}

write_versions() {
  cat > "${work}/versions.yaml" <<EOF
daml-sdk: 3.6.0
daml-sdk-sha256: ${1:-${sdk_digest}}
damlc-sha256: ${2:-${damlc_digest}}
daml-script-sha256: ${3:-${daml_script_digest}}
splice: 0.9.1
EOF
}

reset_registry() {
  find "${FAKE_REGISTRY}" -type f -delete
  publish_manifest 3.6.0 "${sdk_digest}" codegen=3.6.2 daml-script=3.6.2 damlc=3.6.2
  publish_component_tag damlc 3.6.2 "${damlc_digest}"
  publish_component_tag daml-script 3.6.2 "${daml_script_digest}"
  write_versions
  : > "${work}/github-env"
}

run_script() {
  PATH="${work}/bin:${PATH}" VERSIONS_FILE="${work}/versions.yaml" GITHUB_ENV="${work}/github-env" "${script}" "$@" 2>&1
}

expect() {
  local name="$1" want_status="$2" want_text="$3"
  shift 3
  local output status
  output="$(run_script "$@")"
  status=$?
  if [ "${status}" -ne "${want_status}" ] || [[ "${output}" != *"${want_text}"* ]]; then
    echo "FAIL ${name}: status=${status} output=${output}"
    failures=$((failures + 1))
  else
    echo "ok   ${name}"
  fi
}

expect_env() {
  local name="$1" want_line="$2"
  if grep -qxF "${want_line}" "${work}/github-env"; then
    echo "ok   ${name}"
  else
    echo "FAIL ${name}: GITHUB_ENV lacks '${want_line}': $(tr '\n' ' ' < "${work}/github-env")"
    failures=$((failures + 1))
  fi
}

reset_registry
expect "SDK 3.6.0 ships damlc 3.6.2" 0 "damlc=3.6.2 daml-script=3.6.2" 3.6.0
expect_env "the damlc version is exported" "DAMLC_VERSION=3.6.2"
expect_env "the daml-script version is exported" "DAML_SCRIPT_VERSION=3.6.2"
expect_env "the damlc digest is exported with its algorithm prefix" "DAMLC_DIGEST=sha256:9a25f7f1498ead329fe4dd58e338f34950cff786dbe1f9c106fe4c7df9e7d8b8"
expect_env "the daml-script digest is exported with its algorithm prefix" "DAML_SCRIPT_DIGEST=sha256:c340a6f808e6de2137ea6093e360d83bc3dcf7dc3228ca88b01828d1667c59c1"

reset_registry
publish_manifest 3.6.0 "${sdk_digest}" damlc=3.7.1 daml-script=3.7.4
publish_component_tag damlc 3.7.1 "${damlc_digest}"
publish_component_tag daml-script 3.7.4 "${daml_script_digest}"
expect "damlc and daml-script versions are read independently" 0 "damlc=3.7.1 daml-script=3.7.4" 3.6.0

reset_registry
publish_manifest 3.6.0 "${sdk_digest}" daml-script=3.6.2
expect "a manifest without damlc fails loudly" 1 "lists no 'damlc' component" 3.6.0

reset_registry
publish_manifest 3.6.0 "${sdk_digest}" damlc=3.6.2
expect "a manifest without daml-script fails loudly" 1 "lists no 'daml-script' component" 3.6.0

reset_registry
write_versions "${other_digest}"
expect "a manifest digest that does not resolve fails closed" 1 "does not resolve" 3.6.0

reset_registry
publish_manifest 3.5.12 "${sdk_digest}" damlc=3.5.3 daml-script=3.5.3
expect "a manifest digest pinned for another SDK fails loudly" 1 "carries no 3.6.0.yaml" 3.6.0

reset_registry
publish_manifest 3.5.12 "${sdk_digest}" damlc=3.5.3 daml-script=3.5.3
echo "3.6.0.yaml" > "${FAKE_REGISTRY}/$(registry_key "${manifest_repo}@sha256:${sdk_digest}").name"
expect "a manifest that declares another SDK version fails loudly" 1 "declares SDK 3.5.12, not 3.6.0" 3.6.0

reset_registry
publish_component_tag damlc 3.6.2 "${other_digest}"
expect "a damlc tag that moved off its pinned digest fails closed" 1 "damlc 3.6.2 resolves to sha256:0000000000000000000000000000000000000000000000000000000000000000, not the pinned sha256:9a25f7f1498ead329fe4dd58e338f34950cff786dbe1f9c106fe4c7df9e7d8b8" 3.6.0

reset_registry
publish_component_tag daml-script 3.6.2 "${other_digest}"
expect "a daml-script tag that moved off its pinned digest fails closed" 1 "daml-script 3.6.2 resolves to sha256:0000000000000000000000000000000000000000000000000000000000000000, not the pinned sha256:c340a6f808e6de2137ea6093e360d83bc3dcf7dc3228ca88b01828d1667c59c1" 3.6.0

reset_registry
rm "${FAKE_REGISTRY}/$(registry_key "${component_repo}/damlc:3.6.2").digest"
expect "a damlc tag that does not resolve fails closed" 1 "damlc 3.6.2 does not resolve" 3.6.0

for key in daml-sdk-sha256 damlc-sha256 daml-script-sha256; do
  reset_registry
  sed -i.bak "/^${key}:/d" "${work}/versions.yaml"
  expect "a versions.yaml without ${key} fails closed" 1 "versions.yaml has no '${key}'" 3.6.0
done

reset_registry
write_versions "sha256:${sdk_digest}"
expect "a prefixed digest is refused" 1 "daml-sdk-sha256 must be 64 lowercase hex digits" 3.6.0

reset_registry
write_versions "${sdk_digest}" "${damlc_digest:0:63}"
expect "a short digest is refused" 1 "damlc-sha256 must be 64 lowercase hex digits" 3.6.0

reset_registry
expect "a malformed SDK version is refused" 1 "must match" 3.5

reset_registry
mkdir -p "${work}/no-oras"
for tool in mktemp awk cp rm dirname cat grep sed tr; do ln -s "$(command -v "${tool}")" "${work}/no-oras/${tool}"; done
output="$(PATH="${work}/no-oras" VERSIONS_FILE="${work}/versions.yaml" "${BASH}" "${script}" 3.6.0 2>&1)"
status=$?
if [ "${status}" -ne 1 ] || [[ "${output}" != *"oras is required"* ]]; then
  echo "FAIL missing oras is refused: status=${status} output=${output}"
  failures=$((failures + 1))
else
  echo "ok   missing oras is refused"
fi

exit $((failures > 0 ? 1 : 0))
