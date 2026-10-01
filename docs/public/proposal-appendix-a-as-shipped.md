# Proposal Appendix A, as shipped

The [2026-03 grant proposal](https://github.com/canton-foundation/canton-dev-fund/blob/main/proposals/2026-03-Peaceful%20Studio-csharp-dotnet-sdk.md)'s Appendix A illustrated the developer experience this SDK would deliver, ahead of any of it existing. Some of it undershot what shipped (A.7's reassignment events, framed as forward-looking design intent, are live today); some of it named APIs that were never built the way the proposal sketched them (a `Created` outcome arm, an implicitly-`string`-convertible `Party`, a `TransferAsync` that throws).

This page pairs every one of the proposal's A.1–A.11 snippets with the real, compiling equivalent against the shipped API. Every fenced "shipped" block below is copied verbatim from a test file that compiles against the real packages, and a companion test fails the build if this page's text drifts from that file by even a character.

## A.1 — Type-safe contracts from Daml codegen

The proposal showed:

```csharp
public sealed record Iou(
    Party Issuer,
    Party Owner,
    string Currency,
    decimal Amount) : ITemplate
{
    public sealed record IouContractId(string contractId) : ContractId<Iou>(contractId)
    {
        // Generated method — no magic strings in application code
        public ExerciseCommand ExerciseTransfer(Party newOwner) =>
            Transfer.Exercise(this, new TransferArgument(newOwner));
    }
}
```

and described `Party` as "a zero-allocation `readonly record struct` with implicit conversion to `string` ... and explicit conversion from `string`":

```csharp
public readonly record struct Party(string Id)
{
    public static implicit operator string(Party p) => p.Id;
    public static explicit operator Party(string id) => new(id);
}
```

Shipped, the generated `Iou` record has no nested `IouContractId` type:

<!-- shipped: A1_IouRecordShape -->
```csharp
public sealed partial record Iou(
    [property: DamlFieldAttribute("issuer")] Party Issuer,
    [property: DamlFieldAttribute("owner")] Party Owner,
    [property: DamlFieldAttribute("currency")] string Currency,
    [property: DamlFieldAttribute("amount")] decimal Amount
) : ITemplate, IHasChoices<Iou>, IDamlRecord<Iou>
```

and `Party`'s constructor and conversions are:

<!-- shipped: A1_PartyConstructor -->
```csharp
    public Party(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, nameof(id));
        _id = id;
    }
```

<!-- shipped: A1_PartyToStringIsExplicit -->
```csharp
    public static explicit operator string(Party party) =>
        party._id ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) Party to string.");
```

<!-- shipped: A1_PartyFromStringIsExplicit -->
```csharp
    public static explicit operator Party(string id) => new(id);
```

**What changed and why.** The runtime's `ContractId<T>` is `sealed` — no per-template `{Template}.ContractId`/`{Template}.Contract` subtype exists any more, because two id types for the same contract used to compare unequal by runtime type even when they held the same string. Construct one directly: `new ContractId<Iou>(contractId)`. `Party`'s conversion to and from `string` are both `explicit`, in both directions — never implicit either way — so a `Party` never silently degrades to a bare string at a logging call site, and a bare string never silently becomes a party.

## A.2 — Creating a contract (type-safe + outcome-typed)

The proposal showed:

```csharp
var alice = new Party("Alice");
var bob = new Party("Bob");
var iou = new Iou(Issuer: alice, Owner: bob, Currency: "USD", Amount: 1000.00m);

// `TryCreateAsync` returns `ExerciseOutcome<ContractId<Iou>>` — never throws on
// expected ledger failures (see A.6). `alice` implicitly converts to `SubmitterInfo`
// for the single-party case.
var outcome = await ledgerClient.TryCreateAsync(iou, alice);
```

Shipped:

<!-- shipped: A2_CreatingAContract -->
```csharp
public static async Task<ExerciseOutcome<ContractId<IouContract>>> A2_CreatingAContract(ILedgerWriter ledgerClient)
    {
        var alice = new Party("Alice::1220a1b2c3");
        var bob = new Party("Bob::1220d4e5f6");
        var iou = new IouContract(Issuer: alice, Owner: bob, Currency: "USD", Amount: 1000.00m);

        return await ledgerClient.TryCreateAsync(iou, alice);
    }
```

**What changed and why.** `TryCreateAsync` itself matches the proposal exactly, including the single-`Party`-argument overload. The one real-world detail the proposal simplified away: a `Party` is a participant-qualified id (`Alice::<fingerprint>`), not a bare display name — `new Party("Alice")` does not resolve against a live participant.

## A.3 — Exercising a choice (typed extension method)

The proposal showed:

```csharp
var contractId = new Iou.IouContractId("00abc123");
var charlie = new Party("Charlie");

// Returns `ExerciseOutcome<ContractId<Iou>>` — Daml's `Transfer` choice creates a single
// successor Iou, so the typed result is just the new contract ID.
var outcome = await contractId.TransferAsync(ledgerClient, newOwner: charlie, actAs: bob);
```

Shipped:

<!-- shipped: A3_ExercisingAChoice -->
```csharp
public static async Task<ExerciseOutcome<TransferResult>> A3_ExercisingAChoice(
        ContractId<IouContract> contractId, ILedgerWriter ledgerClient, Party bob, Party charlie)
    {
        return await contractId.TryTransferAsync(ledgerClient, new IouContract.Transfer(charlie), bob);
    }
```

**What changed and why.** Two drifts. First, the generated extension is `TryTransferAsync`, not `TransferAsync`: every generated method that returns an `ExerciseOutcome<T>` instead of throwing carries a `Try` prefix, so the name itself tells a caller not to expect an exception on a Daml-level failure. Second, the choice argument is a generated record (`new IouContract.Transfer(charlie)`), not named parameters on the extension method, and the result is `ExerciseOutcome<TransferResult>` — a codegen-emitted record wrapping the choice's actual return type (here, `TransferResult.Iou : ContractId<Iou>`) — not a bare `ExerciseOutcome<ContractId<Iou>>`.

## A.4 — Multi-party workflow

The proposal showed:

```csharp
// Accept an offer — typed `<Choice>Async` extension. `AcceptResult` is a codegen-emitted
// record with one strongly-typed `ContractId<T>` per template the choice creates.
var outcome = await offerContractId.AcceptAsync(ledgerClient, counterpartyParty);

switch (outcome)
{
    case ExerciseOutcome<AcceptResult>.Created(var result):
        var agreementCid = result.Agreement;          // typed ContractId<Agreement>
        var auditCid    = result.AgreementRecord;     // typed ContractId<AgreementRecord>
        // continue with typed CIDs — no JSON walking, no template-id matching
        break;

    case ExerciseOutcome<AcceptResult>.DamlError { ErrorId: "OFFER_EXPIRED" }:
        return Conflict("Offer has expired");

    case ExerciseOutcome<AcceptResult>.InfraError { StatusCode: var status }:
        return StatusCode(503, $"Ledger unavailable: {status}");
}
```

Shipped (using the same `Iou`/`Transfer` vocabulary as the rest of this page, since `AcceptResult`/`Agreement` never shipped):

<!-- shipped: A4_MultiPartyWorkflowOutcomeHandling -->
```csharp
public static async Task<string> A4_MultiPartyWorkflowOutcomeHandling(
        ContractId<IouContract> offerContractId, ILedgerWriter ledgerClient, Party counterparty)
    {
        var outcome = await offerContractId.TryTransferAsync(ledgerClient, new IouContract.Transfer(counterparty), counterparty);

        return outcome switch
        {
            ExerciseOutcome<TransferResult>.One(var result) => $"created {result.Iou.Value}",

            ExerciseOutcome<TransferResult>.DamlError { ErrorId: "OFFER_EXPIRED" } =>
                "409: offer has expired",

            ExerciseOutcome<TransferResult>.InfraError { Status: TransportStatus.Grpc { StatusCode: var code } } =>
                $"503: ledger unavailable ({code})",

            ExerciseOutcome<TransferResult>.CommittedUndecodable u =>
                $"202: committed in update {u.UpdateId}, do not retry: reconcile against the ledger",

            _ => "unexpected outcome",
        };
    }
```

**What changed and why.** The success arm is `ExerciseOutcome<T>.One`, not `.Created` — the outcome type was named `One`/`None`/`Many` from the moment it shipped upstream in `Daml.Runtime` 0.1.4, precisely to describe "how many contracts of the requested shape resulted," which reads correctly for both a create and a choice exercise; `Created` never existed as an arm name. `InfraError.StatusCode` is a nested `TransportStatus` discriminated union (`Grpc`/`Http`/`NoResponse`/`UndecodableBody`), not a flat status code, because an infrastructure failure can come from either the gRPC or the REST transport and the two have different status vocabularies. The `CommittedUndecodable` arm is spelled out rather than left to the discard: the command committed but its result could not be decoded, so the handler surfaces the `UpdateId` for reconciliation and must not invite a retry of an exercise that already consumed the contract. A.11 shows the full treatment, including a stable command id that makes the `503` safe to retry.

## A.5 — Multi-party submissions and `readAs`

The proposal showed:

```csharp
var submitter = new SubmitterInfo(
    actAs: new HashSet<Party> { platformParty, initiatorParty },
    readAs: new HashSet<Party> { observerParty });

var submission = CommandsSubmission
    .Single(CreateCommand.For(offer))
    .WithSubmitter(submitter)
    .WithWorkflowId("my-workflow");

var outcome = await ledgerClient.TrySubmitAndWaitForTransactionAsync(submission, ct);
```

and claimed "`Party` and `string` both implicitly convert to `SubmitterInfo`."

Shipped:

<!-- shipped: A5_MultiPartySubmissionWithReadAs -->
```csharp
public static async Task<ExerciseOutcome<TransactionResult>> A5_MultiPartySubmissionWithReadAs(
        ILedgerWriter ledgerClient, IouContract offer, Party platformParty, Party initiatorParty, Party observerParty, CancellationToken ct)
    {
        var submitter = new SubmitterInfo(
            actAs: new HashSet<Party> { platformParty, initiatorParty },
            readAs: new HashSet<Party> { observerParty });

        var submission = CommandsSubmission
            .Single(CreateCommand.For(offer))
            .WithSubmitter(submitter)
            .WithOptionalWorkflowId("my-workflow");

        return await ledgerClient.TrySubmitAndWaitForTransactionAsync(submission, submitter, cancellationToken: ct);
    }
```

**What changed and why.** Three drifts. `SubmitterInfo` has an implicit conversion from `Party` only — never from `string` — so a raw string still has to be turned into a `Party` first, keeping the compile-time guarantee the proposal wanted for the single-party case without extending it somewhere it would swallow a typo. `CommandsSubmission.WithWorkflowId` takes the `WorkflowId` value type; the plain-string convenience is the separate `WithOptionalWorkflowId(string?)` used above. And `TrySubmitAndWaitForTransactionAsync` takes the `SubmitterInfo` as an explicit second argument even when `.WithSubmitter(...)` was already called on the submission — the two-argument `(submission, ct)` call the proposal shows does not compile, because a bare `CancellationToken` cannot bind to a `SubmitterInfo` parameter.

## A.6 — Structured outcome handling

The proposal showed:

```csharp
ExerciseOutcome<ContractId<Iou>> outcome = await ledgerClient.TryCreateAsync(iou, alice);

return outcome switch
{
    // Happy path — typed CID lifted out
    ExerciseOutcome<ContractId<Iou>>.Created(var cid) => Ok(cid.Value),

    // Daml-level error: structured Canton error category + Daml-defined error id + metadata
    ExerciseOutcome<ContractId<Iou>>.DamlError { Category: ContentionOnSharedResources } =>
        StatusCode(409, "ledger contention — retry"),

    ExerciseOutcome<ContractId<Iou>>.DamlError e =>
        Problem(detail: e.Message, statusCode: 400, type: e.ErrorId),

    // Infrastructure error: gRPC status not interpreted by Daml
    ExerciseOutcome<ContractId<Iou>>.InfraError { StatusCode: Aborted } =>
        StatusCode(503, "ledger unavailable"),

    _ => Problem("unexpected outcome")
};
```

Shipped:

<!-- shipped: A6_StructuredOutcomeHandling -->
```csharp
public static string A6_StructuredOutcomeHandling(ExerciseOutcome<ContractId<IouContract>> outcome) =>
        outcome switch
        {
            ExerciseOutcome<ContractId<IouContract>>.One ok => ok.Result.Value,

            ExerciseOutcome<ContractId<IouContract>>.DamlError { Category: DamlErrorCategory.ContentionOnSharedResources } =>
                "409: ledger contention - retry",

            ExerciseOutcome<ContractId<IouContract>>.DamlError e => $"400: {e.Message} ({e.ErrorId})",

            ExerciseOutcome<ContractId<IouContract>>.InfraError { Status: TransportStatus.Grpc { StatusCode: GrpcStatusCode.Aborted } } =>
                "503: ledger unavailable",

            ExerciseOutcome<ContractId<IouContract>>.CommittedUndecodable u =>
                $"202: committed in update {u.UpdateId}, do not retry: reconcile against the ledger",

            _ => "unexpected outcome",
        };
```

**What changed and why.** Same `Created`→`One` correction as A.4. `Aborted` is the `Daml.Runtime.Outcomes.GrpcStatusCode` enum member, reached through the nested `TransportStatus.Grpc` case, not a bare status name in scope — see A.4's `TransportStatus` note. `DamlErrorCategory.ContentionOnSharedResources` is likewise qualified, since nothing `using static`s the enum in a real call site.

## A.7 — Typed subscription streams

The proposal showed:

```csharp
// Stream every Iou involving Alice, starting from the offset she last persisted.
await foreach (var contractEvent in ledgerClient.SubscribeAsync<Iou>(
    submitter: alice,
    fromOffset: lastSeenOffset,
    ct))
{
    switch (contractEvent)
    {
        case ContractEvent<Iou>.Created(var cid, var iou, var offset):
            await OnIouCreated(cid, iou);
            await PersistOffset(offset);
            break;

        case ContractEvent<Iou>.Archived(var cid, var offset):
            await OnIouArchived(cid);
            await PersistOffset(offset);
            break;

        case ContractEvent<Iou>.StreamError(var status, var msg):
            // surfaced in-band — no try/catch needed for transient stream failures
            log.Warning("Stream error {Status}: {Message}", status, msg);
            return;
    }
}
```

and, separately, framed reassignment as forward-looking: "**Reassignment (multi-synchronizer, design intent under M2 ...)**. `ContractEvent<T>` will gain both as typed cases alongside `Created` and `Archived` ... The cases land under M2; the API shape above is design intent, not shipped capability":

```csharp
case ContractEvent<Iou>.Unassigned(var cid, var sourceSync, var offset):
    await OnIouLeftSynchronizer(cid, sourceSync);
    await PersistOffset(offset);
    break;

case ContractEvent<Iou>.Assigned(var cid, var iou, var targetSync, var offset):
    await OnIouArrivedFromOtherSynchronizer(cid, iou, targetSync);
    await PersistOffset(offset);
    break;
```

Shipped:

<!-- shipped: A7_TypedSubscriptionStreams -->
```csharp
public static async Task A7_TypedSubscriptionStreams(
        ILedgerStreamer ledgerClient, Party alice, LedgerOffset? lastSeenOffset, CancellationToken ct)
    {
        await foreach (var contractEvent in ledgerClient.SubscribeAsync<IouContract>(
            submitter: alice,
            fromOffset: lastSeenOffset,
            cancellationToken: ct))
        {
            switch (contractEvent)
            {
                case ContractStreamEvent<IouContract>.Created created:
                    await OnIouCreatedAsync(created.ContractId, created.Payload);
                    break;

                case ContractStreamEvent<IouContract>.Archived archived:
                    await OnIouArchivedAsync(archived.ContractId);
                    break;

                case ContractStreamEvent<IouContract>.Unassigned unassigned:
                    await OnIouLeftSynchronizerAsync(unassigned.ContractId, unassigned.Source);
                    break;

                case ContractStreamEvent<IouContract>.Assigned assigned:
                    await OnIouArrivedFromOtherSynchronizerAsync(assigned.ContractId, assigned.Payload, assigned.Target);
                    break;

                case ContractStreamEvent<IouContract>.Checkpoint checkpoint:
                    await PersistOffsetAsync(checkpoint.Offset);
                    break;

                case ContractStreamEvent<IouContract>.Unclassified
                {
                    Kind: UnclassifiedKind.DecodeFailure
                        or UnclassifiedKind.MissingSynchronizerId
                        or UnclassifiedKind.InterfaceViewUnavailable
                        or UnclassifiedKind.AssignedEvent
                        or UnclassifiedKind.Unknown
                } unprojected:
                    LogUnprojectedEvent(unprojected.Kind, unprojected.Offset);
                    return;

                case ContractStreamEvent<IouContract>.Unclassified unclassified:
                    LogUnclassifiedEvent(unclassified.Kind, unclassified.RawKind);
                    break;

                case ContractStreamEvent<IouContract>.StreamError error:
                    LogStreamError(error.Status, error.Message);
                    return;
            }
        }
    }
```

**What changed and why.** The type is `ContractStreamEvent<T>`, not `ContractEvent<T>`, and its cases are ordinary type patterns (`Created created`) rather than positional deconstruction — `Created`/`Archived`/`Assigned`/`Unassigned` carry more members than the proposal's tuples show, including `WitnessParties` and, on `Assigned`, a `ContractKey?`. The resume offset is persisted only from `Checkpoint`, never from a contract event. A transaction with several matching events emits each of them with the same update offset, so persisting `created.Offset` as soon as the first one is handled means a crash before the rest (or an unprojectable event later in that transaction) makes the next resume skip them, because `SubscribeAsync`'s lower bound is exclusive. A `Checkpoint` is a participant marker emitted between updates on a participant-configured cadence, so everything before its offset has already been delivered. The cost is that a restart replays the events since the last checkpoint, so the handlers must be idempotent, which a resume pattern needs anyway. The `Unclassified` arms never persist an offset either. A `DecodeFailure`, `MissingSynchronizerId` or `InterfaceViewUnavailable` is a matching event this layer couldn't project. `Unknown` is a wire variant this SDK version doesn't recognise, so it may be one too. `AssignedEvent` covers both an assignment for another template and an assignment delivered without its `CreatedEvent`, and the second one carries no template id to rule out an `Iou`. The subscription is template-filtered on the participant, so an assignment for another template isn't expected on it in the first place; each is surfaced rather than silently dropped, and its `Offset` is the containing update's own offset, and persisting it (or any later offset) would make the next resume skip that update for good. So the snippet stops the subscription there: the persisted offset stays before the unprojected update, and a restart re-reads it once the cause (typically a codegen/DAR version mismatch) is fixed. The remaining kinds are provably not an `Iou` the handlers missed — a create, archive, exercise or unassignment event for a different template, or a reassignment carrying neither side — so the snippet logs them and carries on — the next `Checkpoint` advances the offset past them. A `null` `Offset` must never be substituted with a default either: `LedgerOffset`'s default is `Begin`, so doing so would re-read the whole ledger. More importantly: **`Assigned` and `Unassigned` are shipped today, not design intent.** They were added to the outcome-typed API early on, and later made to actually flow end-to-end through `SubscribeAsync`/`SubscribeActiveAsync` (the live subscription request wasn't asking the participant for reassignment events, so they were silently never delivered). The proposal's own multi-synchronizer section under-describes current capability, not the other way around.

## A.8 — Daml interfaces and Token Standard

The proposal showed:

```csharp
// `Holding` is a Splice Token Standard interface. The concrete implementing template
// (CantonCoin.Holding, Stablecoin.Holding, etc.) is irrelevant to the caller.
ContractId<Holding> holding = activeContracts.Single<Holding>();

// Exercise an interface choice — same call shape as a template choice.
var outcome = await holding.TransferAsync(
    ledgerClient,
    newOwner: bob,
    actAs: alice);
```

Shipped (`IExampleHolding` stands in for a real Splice interface such as `IHolding` — the call shape is identical):

<!-- shipped: A8_InterfacesAndTokenStandard -->
```csharp
public static async Task<ContractId<IExampleHolding>> A8_InterfacesAndTokenStandard(
        ContractId<IExampleHolding> holding, ILedgerWriter ledgerClient, Party alice, Party bob)
    {
        var outcome = await holding.TryTransferAsync(ledgerClient, new ExampleHoldingTransfer(bob), alice);

        return outcome switch
        {
            ExerciseOutcome<ContractId<IExampleHolding>>.One ok => ok.Result,
            ExerciseOutcome<ContractId<IExampleHolding>>.CommittedUndecodable u => throw new InvalidOperationException(
                $"Transfer committed in update {u.UpdateId} but its result could not be decoded; reconcile, do not resubmit.",
                u.SourceException),
            _ => throw new InvalidOperationException(outcome.GetType().Name),
        };
    }
```

**What changed and why.** The emitter generates a typed `I<InterfaceName>Extensions.Try<ChoiceName>Async` extension for every choice an interface declares — shown here as `TryTransferAsync`, backed by `IExampleHolding.ChoiceTransfer`, exactly the same shape as `IHoldingExtensions.TryArchiveAsync` in the conformance corpus. That extension dispatches through `ExerciseCommand.For<TOwner>`, which resolves the wire `template_id` field from whichever type owns the choice — the interface's own `InterfaceId` here — the same mechanism a concrete template's generated extension uses; this is what the proposal's "same call shape as a template choice" claim actually looks like once it compiles. One boundary is worth calling out precisely because it trips people up: the Ledger API only resolves a choice through an interface id when that interface itself declares the choice. A choice a concrete implementing template adds on top of an interface it implements has no interface-typed call at all — generated or hand-rolled — and needs the concrete template's own contract id and identifier instead. A committed transfer whose result could not be decoded surfaces as `CommittedUndecodable`, and the snippet carries its `UpdateId` into the exception instead of folding it into the generic discard, so the caller can reconcile rather than resubmit.

## A.9 — Querying active contracts via PQS

The proposal showed three examples:

```csharp
var alice = new Party("Alice");

var contracts = await pqsClient.QueryAsync<Iou>(
    Filter.Or(
        Filter.Field<Iou>(i => i.Issuer, alice),
        Filter.Field<Iou>(i => i.Owner, alice)),
    ct);

// Each result has .Id (ContractId) and .Data (the typed Iou record)
var ious = contracts.Select(c => c.Data).ToList();
```

```csharp
var contract = await pqsClient.FetchByIdAsync(new ContractId<Iou>(contractId), ct);
var iou = contract?.Data;  // null if archived or not found
```

```csharp
var contract = await pqsClient.QueryOneAsync<Iou>(
    Filter.And(
        Filter.Field<Iou>(i => i.Issuer, alice),
        Filter.Field<Iou>(i => i.Owner, bob)),
    ct);
```

Shipped:

<!-- shipped: A9_FindAllIousInvolvingAlice -->
```csharp
public static async Task<IReadOnlyList<IouContract>> A9_FindAllIousInvolvingAlice(
        IPqsClient pqsClient, Party alice, CancellationToken ct)
    {
        var contracts = await pqsClient.QueryAsync<IouContract>(
            Filter.Or(
                Filter.Field<IouContract>(i => i.Issuer, alice.Value),
                Filter.Field<IouContract>(i => i.Owner, alice.Value)),
            ct);

        return contracts.Select(c => c.Data).ToList();
    }
```

<!-- shipped: A9_FetchContractById -->
```csharp
public static async Task<IouContract?> A9_FetchContractById(
        IPqsClient pqsClient, string contractId, CancellationToken ct)
    {
        var contract = await pqsClient.FetchByIdAsync(new ContractId<IouContract>(contractId), ct);
        return contract?.Data;
    }
```

<!-- shipped: A9_CombineFiltersWithAnd -->
```csharp
public static async Task<IouContract?> A9_CombineFiltersWithAnd(
        IPqsClient pqsClient, Party alice, Party bob, CancellationToken ct)
    {
        var contract = await pqsClient.QueryOneAsync<IouContract>(
            Filter.And(
                Filter.Field<IouContract>(i => i.Issuer, alice.Value),
                Filter.Field<IouContract>(i => i.Owner, bob.Value)),
            ct);

        return contract?.Data;
    }
```

**What changed and why.** `Filter.Field<T>`'s value parameter is `string`, not `Party` — pass `alice.Value` (the explicit `string` conversion from A.1) rather than the `Party` itself. `FetchByIdAsync`/`QueryOneAsync` are otherwise exactly as shown.

## A.10 — Service registration and health checks

The proposal showed:

```csharp
// Program.cs — register Canton clients from configuration
services.AddLedgerClient(config.GetSection("Canton:Ledger"));  // gRPC commands
services.AddPqsClient(config.GetSection("Canton:Pqs"));        // PQS queries

// Health checks provided by the Canton packages
builder.Services.AddHealthChecks()
    .AddPqsClient(tags: ["ready"])
    .AddLedgerClient(tags: ["ready"]);
```

Shipped:

<!-- shipped: A10_ServiceRegistration -->
```csharp
public static void A10_ServiceRegistration(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLedgerClient(configuration.GetSection("Canton:Ledger"));
        services.AddPqsClient(configuration.GetSection("Canton:Pqs"));

        services.AddHealthChecks()
            .AddPqsClient(tags: ["ready"])
            .AddLedgerClient(tags: ["ready"]);
    }
```

**What changed and why.** No drift — this is the one Appendix A example the proposal got right end to end, down to the parameter names.

## A.11 — Putting it all together: a request handler

The proposal showed a complete ASP.NET Core controller:

```csharp
[ApiController]
[Route("api/v1/ious")]
public class IouController(IouQueries queries, ILedgerClient ledger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMyIous(CancellationToken ct)
    {
        // Party constructed once at the API boundary from the auth header
        var party = new Party(Request.Headers["X-Party-Id"].First()!);
        var ious = await queries.GetIousByPartyAsync(party, ct);
        return Ok(ious);
    }

    [HttpPost("{contractId}/transfer")]
    public async Task<IActionResult> Transfer(
        string contractId, TransferRequest req, CancellationToken ct)
    {
        var party    = new Party(Request.Headers["X-Party-Id"].First()!);
        var cid      = new Iou.IouContractId(contractId);
        var newOwner = new Party(req.NewOwner);

        // Typed extension on the contract ID + structured outcome (see A.3, A.6).
        var outcome = await cid.TransferAsync(ledger, newOwner, actAs: party, ct);

        return outcome switch
        {
            ExerciseOutcome<ContractId<Iou>>.Created(var newCid) =>
                Ok(new { contractId = newCid.Value }),

            ExerciseOutcome<ContractId<Iou>>.DamlError e =>
                Problem(detail: e.Message, statusCode: 400, type: e.ErrorId),

            ExerciseOutcome<ContractId<Iou>>.InfraError e =>
                StatusCode(503, e.Message),

            _ => Problem("unexpected outcome")
        };
    }
}
```

Shipped, as the transfer handler's core logic (this page's compile harness has no ASP.NET Core dependency, so the framework attributes and `IActionResult` wrapping are left to the reader — the ledger calls below are exactly what the controller action would run):

<!-- shipped: A11_HandlerPuttingItAllTogether -->
```csharp
public static async Task<string> A11_HandlerPuttingItAllTogether(
        ILedgerWriter ledgerClient,
        string partyHeaderValue,
        string idempotencyKeyHeaderValue,
        string contractIdValue,
        string newOwnerValue,
        CancellationToken ct)
    {
        var party = new Party(partyHeaderValue);
        var contractId = new ContractId<IouContract>(contractIdValue);
        var newOwner = new Party(newOwnerValue);
        var commandId = new CommandId(idempotencyKeyHeaderValue);

        var outcome = await contractId.TryTransferAsync(
            ledgerClient, new IouContract.Transfer(newOwner), party, commandId: commandId, cancellationToken: ct);

        return outcome switch
        {
            ExerciseOutcome<TransferResult>.One(var result) => $"200: {result.Iou.Value}",

            ExerciseOutcome<TransferResult>.DamlError { ErrorId: "DUPLICATE_COMMAND" } =>
                $"409 (already submitted under idempotency key {commandId}, do not retry): reconcile against the ledger",

            ExerciseOutcome<TransferResult>.DamlError e => $"400: {e.Message} ({e.ErrorId})",

            ExerciseOutcome<TransferResult>.InfraError e => $"503: {e.Message}",

            ExerciseOutcome<TransferResult>.CommittedUndecodable u =>
                $"202 (committed, do not retry — reconcile by update id {u.UpdateId ?? "(unknown)"}): {u.Message}",

            ExerciseOutcome<TransferResult>.None or ExerciseOutcome<TransferResult>.Many =>
                "202 (committed, do not retry — reconcile against the ledger): unexpected result shape",

            _ => throw new UnreachableException($"Unexpected outcome {outcome.GetType().Name}."),
        };
    }
```

**What changed and why.** Same `IouContractId`→`ContractId<Iou>`, `TransferAsync`→`TryTransferAsync` and `Created`→`One` drifts as A.1–A.4 and A.6, now composed in one call site. The `CommittedUndecodable` arm is explicit here because the SDK's own doc for that outcome says *do not resubmit* and provides an `UpdateId` for out-of-band reconciliation — and the response says `202`, not `500`: a caller or proxy that retries 5xx responses by convention would otherwise resubmit a command already known to have committed. `None` and `Many` get the same `202`: `ExerciseOutcomeProjection.ProjectCommitted` propagates them precisely so a committed transaction is never misread as a submission failure and resubmitted. With all six `ExerciseOutcome<T>` variants handled, there is no catch-all `500` left for a committed command to fall into; the discard arm only exists because the compiler can't prove the record hierarchy closed, and throws `UnreachableException` the same way `ProjectCommitted` itself does. `202` signals "accepted, outcome pending reconciliation" without implying success or inviting a retry. `InfraError` stays a retryable `503` because the command id is no longer minted per call: it comes from the request's idempotency key, so an `InfraError` whose submission actually committed (a timeout after the participant accepted it, say) is safe to retry with the same key. Ledger-side deduplication then rejects the resubmission with `DUPLICATE_COMMAND` instead of running the transfer again, and the handler answers that with a non-retryable `409`. The party is shown constructed from a header value to keep the compile harness self-contained; in production, the acting party must come from the authenticated user's claims — passing an unvalidated caller-supplied value lets any party the service token has `actAs` rights for be selected by the requester. The ASP.NET Core wrapping itself (`[ApiController]`, `Ok(...)`, `Problem(...)`) is a framework choice orthogonal to the SDK and isn't part of what this page's compile guard needs to pin.
