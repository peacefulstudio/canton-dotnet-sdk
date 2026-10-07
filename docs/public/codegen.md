# Code generation

What `dpm codegen-cs` emits from a Daml `.dar`, how to run it, what a generated template looks like, and how to assemble commands by hand.

Back to the [README](../../README.md).

## Features

- **Strongly-typed contracts**: Generated record types for all Daml templates and data types
- **Choice support**: Typed choice arguments and results, and `Try<Choice>Async` extension methods with one `Party` parameter per controller
- **Interfaces, contract keys and upgrades**: Interface views and implementations, contract-key types and by-key commands, and an upgrade marker on every template
- **JSON serialization**: Built-in support for the JSON Ledger API
- **Modern C#**: Uses C# 12+ features (records, primary constructors, file-scoped namespaces); key-bearing DARs require C# 13 (`partial` property support — .NET 9 SDK or later on the build machine)
- **Nullable reference types**: Full nullable annotation support
- **Cross-platform**: Works on Windows, macOS, and Linux
- **NuGet pipeline**: Generate complete `.csproj` files for publishing DAR dependencies as NuGet packages

## Generate Code

```bash
# Generate C# from a DAR file
dpm codegen-cs --dar ./my-project.dar --out ./generated -n MyCompany.Contracts

# With verbose output
dpm codegen-cs --dar ./my-project.dar --out ./generated -v 2
```

> **Architecture note.** `dpm codegen-cs` accepts a `.dar` because the OCI
> bundle pairs a DAR → IntermediateDar decoder (a JVM helper) with the C#
> emitter from this repo. The emitter CLI in this repo consumes only the
> decoded IntermediateDar proto (`--intermediate`); it cannot read a `.dar`
> directly. See [CLI Reference](cli-reference.md).

## Building Commands by Hand

The generated `Try…Async` methods cover the common path. To assemble a submission yourself —
several commands in one transaction, or a workflow id — build the commands from the generated
types (this is the compiled [`samples/QuickstartExample`](../../samples/QuickstartExample/Program.cs)):

```csharp
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using IouContract = Iou.Iou;

var alice = new Party("Alice::1220deadbeef");
var bob = new Party("Bob::1220deadbeef");
var charlie = new Party("Charlie::1220deadbeef");

var iou = new IouContract(
    Issuer: alice,
    Owner: bob,
    Currency: "USD",
    Amount: 1000.00m
);

var createCmd = CreateCommand.For(iou);

var contractId = new ContractId<IouContract>("00abc123");
var transferCmd = ExerciseCommand.For(
    contractId,
    IouContract.ChoiceTransfer.Name,
    new IouContract.Transfer(NewOwner: charlie).ToRecord());

var submission = CommandsSubmission.Single(createCmd)
    .WithActAs(alice)
    .WithWorkflowId(new WorkflowId("iou-issuance"));
```

Once a submission commits, don't scan the resulting transaction's
`CreatedContracts` by hand — the `Daml.Runtime.Contracts.TransactionResultExtensions`
helpers project it back to typed contract ids:

```csharp
using Daml.Runtime.Contracts;

// tx is the TransactionResult of a committed submission
ContractId<IouContract> issued = tx.Single<IouContract>();     // throws unless exactly one Iou was created
ContractId<IouContract>? maybe = tx.TrySingle<IouContract>();  // null when none, throws on more than one
```

`All<T>()` returns every created contract of a type, in transaction order;
see [`src/Daml.Runtime/README.md`](../../src/Daml.Runtime/README.md) for the
worked example and the upgrade-safe matching rules.

## Generated Code Example

Given this Daml template (the model vendored at
[`samples/QuickstartExample/daml/Iou.daml`](../../samples/QuickstartExample/daml/Iou.daml)):

```daml
template Iou
  with
    issuer : Party
    owner : Party
    currency : Text
    amount : Decimal
  where
    signatory issuer
    observer owner

    choice Transfer : ContractId Iou
      with
        newOwner : Party
      controller owner
      do create this with owner = newOwner
```

the codegen produces (abridged from the checked-in emitter output at
[`samples/QuickstartExample/Generated/Iou/Iou.cs`](../../samples/QuickstartExample/Generated/Iou/Iou.cs);
elisions marked `…`; the emitter writes every type reference rooted at `global::`, such as
`global::Daml.Runtime.Data.Party`, so no Daml name can shadow one, and the listing drops that
prefix for readability). The C# namespace is the Daml module name, so the `Iou` module lands in
`namespace Iou;` — from another namespace the template is `Iou.Iou`, or bind an alias such as
`using IouContract = Iou.Iou;`:

```csharp
namespace Iou;

/// <summary>
/// Generated from Daml template Iou:Iou
/// </summary>
public sealed partial record Iou(
    [property: DamlFieldAttribute("issuer")] Party Issuer,
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("currency")] string Currency,
    [property: DamlFieldAttribute("amount")] decimal Amount
) : ITemplate, IHasChoices<Iou>, IDamlRecord<Iou>
{
    /// <summary>Gets the template identifier.</summary>
    public static Identifier TemplateId { get; } = new("61d1c8472218a119a9e73167b9e9af82bfed91bf5ae5a89a82da27c10ea7f763", "Iou", "Iou");

    // … package id/name/version properties, Daml-LF JSON decoder and Archive choice metadata elided …

    /// <summary>Converts this value to a DamlRecord.</summary>
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("issuer", Issuer.ToDamlValue()),
        DamlField.Create("owner", Owner.ToDamlValue()),
        DamlField.Create("currency", new DamlText(Currency)),
        DamlField.Create("amount", new DamlNumeric(Amount))
    );

    /// <summary>Creates an instance from a DamlRecord.</summary>
    public static Iou FromRecord(DamlRecord record) => new Iou(
        Issuer: Party.FromDamlValue(record.GetRequiredField("issuer").As<DamlParty>()),
        Owner: Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
        Currency: record.GetRequiredField("currency").As<DamlText>().Value,
        Amount: record.GetRequiredField("amount").As<DamlNumeric>().Value
    );

    /// <summary>
    /// Exercise the Transfer choice.
    /// This choice is consuming and will archive the contract.
    /// </summary>
    public static Choice<Iou, Transfer, ContractId<Iou>> ChoiceTransfer { get; } = new()
    {
        Name = new ChoiceName("Transfer"),
        Consuming = true,
        ArgumentEncoder = arg => arg.ToRecord(),
        ArgumentDecoder = val => Transfer.FromRecord(val.As<DamlRecord>()),
        ResultDecoder = val => new ContractId<Iou>(val.As<DamlContractId>().Value),
        // …
    };
}
```

The same file also emits
`IouExtensions.TryTransferAsync` and `IouSubmissionExtensions.TryCreateAsync`
extension methods that submit through an `ILedgerWriter` (which every `ILedgerClient`
is); the choice-argument record `Iou.Transfer` is emitted alongside in
[`Iou.Transfer.cs`](../../samples/QuickstartExample/Generated/Iou/Iou.Transfer.cs).
See [`samples/QuickstartExample`](../../samples/QuickstartExample/Program.cs) for a
complete, runnable rendition of this shape.

## Registration

Each Daml package also gets one generated file, `PackageRegistration_<package id>.cs`, holding an
internal `[ModuleInitializer]` that registers the package's templates, interfaces and choices with
`GeneratedTypeReaders` when its module is first used. The JSON Ledger API client finds generated
types only through that registry, so bindings generated by an earlier release are carried as raw
Daml-LF JSON (`DamlUndecodedJson`) rather than decoded: regenerate every binding you read over the
JSON Ledger API, dependency packages included, with this release's codegen.

## Choice results

A template choice that returns contract ids returns exactly what the choice returned:
`ContractId T` is `ExerciseOutcome<ContractId<T>>`, `Optional (ContractId T)` is
`ExerciseOutcome<ContractId<T>?>`, `[ContractId T]` is
`ExerciseOutcome<IReadOnlyList<ContractId<T>>>`, and a tuple is `ExerciseOutcome<Tuple2<A, B>>`,
read through `_1` and `_2`. The result is read from the choice's own exercise, so a contract the
same transaction created for another reason cannot change it, and there is no generated
`<Choice>Result` wrapper record. Generated code needs the matching `Daml.Runtime` version:
regenerate your bindings whenever you upgrade it.

## Names that clash

Codegen keeps a Daml name as the C# name wherever it compiles. A record, variant or template named
like one of its generated members (`ToRecord`, `FromRecord`, `Equals`, `Tag`, `TemplateId` and the
rest), or a record or template field named like a generated property, is emitted with one trailing
underscore (`ToRecord_`) and every reference follows. Each rename is a codegen warning naming the
package, module, entity and emitted name; the Daml name, the template id and the wire names do not
change. Where two Daml names would make the generated types clash and no rename can fix it, such as
a type named like the `<T>Extensions` class of another declaration, a variant constructor named like
a member of its variant, or a module namespace spelled like a type another module emits, codegen
stops with an error naming both Daml identifiers: rename one of them in Daml.
