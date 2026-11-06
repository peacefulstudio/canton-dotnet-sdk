#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
site_dir="${repo_root}/docs/public/site"
site_output="${site_dir}/_site"
markdown_api="${site_dir}/api-markdown"
markdown_output="${site_output}/llms"

namespace_pages() {
  grep -l '^# .*Namespace ' "${markdown_api}"/*.md | sort
}

namespace_name() {
  head -n 1 "$1" | sed -e 's/^# <a id="[^"]*"><\/a> Namespace //'
}

namespace_section() {
  local prefix="$1" title="$2" page name
  echo "## ${title}"
  echo
  for page in $(namespace_pages); do
    name="$(namespace_name "${page}")"
    case "${name}" in
      ${prefix}*) echo "- [${name}](llms/$(basename "${page}"))" ;;
    esac
  done
  echo
}

write_llms_txt() {
  {
    echo "# Canton .NET SDK"
    echo
    echo "> C# packages for apps that talk to a Canton/Daml ledger with full type safety: Daml codegen, a gRPC and JSON Ledger API client behind one ICantonLedgerClient, PQS, OpenTelemetry and test fakes. This is the public API reference, one Markdown page per namespace and type."
    echo
    echo "## Guides"
    echo
    echo "- [Getting started](articles/getting-started.html)"
    echo "- [Ledger clients quickstart](articles/ledger-clients-quickstart.html)"
    echo "- [Packages](articles/packages.html)"
    echo
    namespace_section "Canton." "Canton ledger client namespaces"
    namespace_section "Daml." "Daml runtime and codegen namespaces"
    namespace_section "OpenTelemetry." "OpenTelemetry namespaces"
    echo "## Optional"
    echo
    echo "- [Full API reference in one file](llms-full.txt)"
  } > "${site_output}/llms.txt"
}

write_llms_full_txt() {
  local page
  for page in "${markdown_api}"/*.md; do
    cat "${page}"
    echo
  done > "${site_output}/llms-full.txt"
}

cd "${repo_root}"
rm -rf "${markdown_api}"
dotnet tool restore
dotnet docfx docfx.json "$@"

rm -rf "${markdown_output}"
mkdir -p "${markdown_output}"
cp "${markdown_api}"/*.md "${markdown_output}/"
write_llms_txt
write_llms_full_txt
