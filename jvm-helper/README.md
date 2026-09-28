# JVM codegen helper

Scala/JVM binary that wraps [`daml-lf-archive`](https://search.maven.org/artifact/com.daml/daml-lf-archive-reader_2.13)
to decode a `.dar` archive and emit an [`IntermediateDar`](../proto/intermediate_dar.proto)
protobuf message to disk.

See `JVM helper` and `AstToIntermediate translator` in [CONTEXT.md](../CONTEXT.md)
for the domain terms. This component is invoked as a child process by the
`dpm codegen-cs` launcher.

## Layout

- `src/main/scala/.../Decode.scala` — CLI entry point; parses `--dar`/`--out`, orchestrates the pipeline. Free of `daml-lf-archive` types.
- `src/main/scala/.../SchemaDecoder.scala` — reads the `.dar`, decodes each package with `onlySerializableDataDefs = true`, then erases expression bodies (schema mode).
- `src/main/scala/.../SignatureErasure.scala` — `Ast.Package` → `Ast.PackageSignature`. One of two files coupled to Digital Asset's own case classes.
- `src/main/scala/.../AstToIntermediate.scala` — `Ast.PackageSignature` → `IntermediateDar` protobuf. The other half of the DA-coupled surface.

When DA renames an internal case class in `daml-lf-archive`, only
`SignatureErasure.scala` and `AstToIntermediate.scala` need to rebase.
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
  --out path/to/output.binpb
```

The output file is a binary-encoded `IntermediateDar` proto message. Parse
it from C# using [`Google.Protobuf`](https://www.nuget.org/packages/Google.Protobuf/)
against the same `intermediate_dar.proto`.
