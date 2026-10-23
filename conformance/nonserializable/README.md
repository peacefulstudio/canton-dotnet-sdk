# `nonserializable` corpus

Declares `data Fn = Fn with f : Int -> Int` — a non-serializable data type whose only
field is a function arrow — beside the `Ledger` template that uses it in a choice body.
Daml-LF keeps `Fn` as a data type with `serializable = false`; the JVM helper's
`SignatureErasure` strips it, so the decoder never hands the emitter a type it would
reject for its signature-only `Arrow` field.

It also declares `data VSyn = VSyn (forall a. ShowEq a => a -> Text) | VSynNone` over a constraint
synonym. damlc interns the constraint tuple as a struct in the package's interned-type pool and the
non-serializable `VSyn` references it, which the JVM helper never sees and a DAR parser must not
reject.

Its oracle `intermediate.binpb`, decoded from this DAR, lives in
`tests/Daml.Codegen.CSharp.Tests/Snapshots/nonserializable/`, refreshed by
`scripts/regenerate.sh write`.

It builds with `--target=2.2` and is the one fixture that keeps Daml-LF 2.2 covered, since the
compiler default is 2.3.

Rebuild after editing `NonSerializable.daml`:

    cd conformance/nonserializable && dpm build && \
      cp .daml/dist/nonserializable-*.dar ./nonserializable.dar

then run `scripts/regenerate.sh write` from the repo root.
