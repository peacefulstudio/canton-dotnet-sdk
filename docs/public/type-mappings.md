# Daml to C# type mappings

How every Daml type maps to a C# type and a `Daml.Runtime` type, including the `Optional`, `Numeric` and `Party` edge cases.

Back to the [README](../../README.md).

| Daml Type | C# Type | Runtime Type |
|-----------|---------|--------------|
| `Int` | `long` | `DamlInt64` |
| `Numeric n` | `decimal` | `DamlNumeric` |
| `Text` | `string` | `DamlText` |
| `Bool` | `bool` | `DamlBool` |
| `Unit` | `DamlUnit` | `DamlUnit` |
| `Party` | `Party` (readonly record struct) | `DamlParty` |
| `Date` | `DateOnly` | `DamlDate` |
| `Time` | `DateTimeOffset` | `DamlTimestamp` |
| `ContractId T` | `ContractId<T>` | `DamlContractId` |
| `Optional a` | `T?`, or `Optional<T>` | `DamlOptional` |
| `Optional (Optional a)` | `Optional<Optional<T>>` | `DamlOptionalChain`, one level per `Optional` |
| `List a` | `IReadOnlyList<T>` | `DamlList` |
| `TextMap a` | `IReadOnlyDictionary<string, T>` | `DamlTextMap` |
| `GenMap k v` | `IReadOnlyDictionary<K, V>` | `DamlGenMap` |
| Record | `record` class | `DamlRecord` |
| Variant | Abstract record + derived | `DamlVariant` |
| Enum | `enum` | `DamlEnum` |

Daml standard-library types map to `Daml.Runtime.Stdlib`, a flat namespace shipped in `Daml.Runtime`, so a
field of one compiles with or without `--include-dependencies`: `Tuple2` to `Tuple20`, `Either`,
`Set`, `Map`, `NonEmpty`, `DayOfWeek`, `Month` (`DA.Date.Types:Month`), `RelTime`, `Ordering`,
`Unit<a>` (`GHC.Tuple.Unit`, the one-element tuple, not `DamlUnit`), `Validation`, `Formula`,
`Sum`, `Product`, `Max`, `Min`, `Down`, `All`, `Any`, `Minstd`, `SrcLoc`, `Archive`,
`ArithmeticError`, `AssertionFailed`, `GeneralError` and `PreconditionFailed`. A simple name such
as `Sum` or `Unit` can be ambiguous (CS0104) with a same-named type from another namespace of your
own generated code when both namespaces are imported in one file.

An `Optional a` maps to `T?` wherever C# nullable syntax can carry it. Four
positions it cannot: an `Optional` over a type variable, an
`Optional` passed as a type argument to a generated generic, an `Optional`
used as a `GenMap` key (`IReadOnlyDictionary`'s key type parameter is
`notnull`), and an `Optional` nested directly inside another `Optional`, where
`T??` does not exist and `Some None` would collapse into `None`. All four map
to `Optional<T>` (`Daml.Runtime.Stdlib`), a `Some`/`None` pair read through
`Match`, `HasValue`, `TryGetValue` or `GetValueOrDefault()`. For the first
three the wire encoding is unchanged, so which representation a field gets
does not change the payload it serializes to. A nested chain is the
exception: every level of it writes the array form — `[]` when absent, `[v]`
when present — which is what a participant accepts in a nested position.

Every `Numeric n` maps to `decimal` whatever the declared scale, which the
generated type does not carry. A value works when it is representable as a
`decimal` and valid for the declared `Numeric n` — at most 38 significant
digits total, `n` of them after the decimal point; the generated type does
not enforce that scale, so an out-of-range value is rejected by the
participant. A participant pads a Numeric out to its declared scale, so a
`Numeric 37` slot carrying `1.5` arrives with 36 trailing zeros; those zeros are
stripped and the narrowing retried, so padding alone never fails. Stripping is
attempted only after the exact narrowing fails, so a mantissa that already fits
keeps the scale it arrived with — `1.50` stays `1.50`.

Two cases throw `OverflowException` from `DamlNumeric.Value` rather than round
silently: a value still needing more than 28 fractional digits once stripped
(`decimal` holds 0-28, Daml-LF allows up to 37), and a magnitude beyond
`decimal.MaxValue`. Magnitude is a separate axis from scale — every `Numeric n`
admits 38 significant digits, so even a `Numeric 0` field can exceed `decimal`.
The runtime type is `BigInteger`-backed and carries any legal Daml-LF Numeric
without loss; only the narrowing to `decimal` can fail.

`Party` serializes as a plain JSON string (not an object) so payloads
round-trip against PQS and the JSON Ledger API; conversions to and from
`string` are explicit so a party can never be silently mistaken for an
arbitrary string.
