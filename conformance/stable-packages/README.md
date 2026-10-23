# Vendored Daml stable packages

The `.dalf` files under `lf-v2/` are the stable packages of the Daml SDK 3.6.0 (damlc component 3.6.2)
(`daml-prim` and `daml-stdlib`), copied unmodified from
`~/.dpm/cache/components/damlc/3.6.2/damlc-dist-dpm/resources/stable-packages/lf-v2/`.

They feed `scripts/regenerate.sh write --only runtime-stdlib`, which generates
`src/Daml.Runtime/Stdlib/Generated`, and the inventory guard in
`tests/Daml.Codegen.DarParser.Tests`.

Refresh on a Daml SDK bump:

```bash
rm -rf conformance/stable-packages/lf-v2
cp -R ~/.dpm/cache/components/damlc/<version>/damlc-dist-dpm/resources/stable-packages/lf-v2 conformance/stable-packages/lf-v2
scripts/regenerate.sh write --only runtime-stdlib
```

then update the version above and commit the result.
