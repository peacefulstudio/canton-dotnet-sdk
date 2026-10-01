# JVM codegen helper

Scala/JVM binary that wraps [`daml-lf-archive`](https://search.maven.org/artifact/com.daml/daml-lf-archive-reader_2.13)
to decode a `.dar` archive and emit an [`IntermediateDar`](../proto/intermediate_dar.proto)
protobuf message to disk.

See `JVM helper` and `AstToIntermediate translator` in [CONTEXT.md](../CONTEXT.md)
for the domain terms. This component is invoked as a child process by the
`dpm codegen-cs` launcher.

## Layout

- `src/main/scala/.../Decode.scala` — CLI entry point; parses `--dar`/`--out`/`--schema-only`/`--version`, orchestrates the pipeline. Free of `daml-lf-archive` types.
- `src/main/scala/.../FullDecoder.scala` — the default path: reads the `.dar` keeping template and choice expression bodies, so the party-expression analysis can run over them.
- `src/main/scala/.../PartyExpressionAnalyzer.scala` and `PartyAnalyses.scala` — the static analysis that records a `Static`/`Dynamic` verdict on each template and choice's signatories, observers and controllers.
- `src/main/scala/.../SchemaDecoder.scala` — the `--schema-only` path: decodes each package with `onlySerializableDataDefs = true`, then erases expression bodies and emits `Dynamic` everywhere.
- `src/main/scala/.../DarDecoders.scala` — the DAR-reading boilerplate both decoders share.
- `src/main/scala/.../SignatureErasure.scala` — `Ast.Package` → `Ast.PackageSignature`. One of the files coupled to Digital Asset's own case classes.
- `src/main/scala/.../AstToIntermediate.scala` — `Ast.PackageSignature` → `IntermediateDar` protobuf. The other DA-coupled file.

When DA renames an internal case class in `daml-lf-archive`, only the files that touch its
AST (`SignatureErasure.scala`, `AstToIntermediate.scala`, the two decoders and the party
analysis) need to rebase.
The on-disk protobuf wire format owned by [`proto/intermediate_dar.proto`](../proto/intermediate_dar.proto)
stays stable.

## Build

```bash
cd jvm-helper
sbt test
sbt assembly
```

The fat JAR is written to `target/scala-2.13/daml-dar-to-proto.jar`.

## Run

```bash
java -jar target/scala-2.13/daml-dar-to-proto.jar \
  --dar path/to/contracts.dar \
  --out path/to/output.binpb \
  [--schema-only]
```

By default the helper runs the static party-expression analysis, which is sensitive to the
Daml-LF patch version. `--schema-only` skips it and emits `Dynamic` verdicts everywhere,
making the output patch-version-insensitive. `--version` prints the build version.

The output file is a binary-encoded `IntermediateDar` proto message. Parse
it from C# using [`Google.Protobuf`](https://www.nuget.org/packages/Google.Protobuf/)
against the same `intermediate_dar.proto`.
