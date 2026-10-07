# Ledger clients

What the `Canton.Ledger.*` client libraries offer and how to use them: ledger and admin clients, PQS, generated types and authentication.

Back to the [README](../../README.md).

## Client Features

### Ledger Client (`Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client`)
- Create contracts from generated Daml template types
- Exercise choices on contracts
- Submit batched commands atomically
- Stream transactions and active-contract snapshots as typed events
- Read a contract's disclosure by id (`GetDisclosureAsync<T>`) to attach it to a submission
- One failure contract on both transports: `LedgerOperationException` from throwing calls, `ExerciseOutcome<T>` from `Try*` calls, a terminal `StreamError` from streams
- Full async/await support

### Admin Client (`Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client`)
- Allocate and manage parties
- Create and manage users
- Grant and revoke user rights

### PQS Client (`Canton.Ledger.Pqs.Client`)
- Query active contracts by template type
- Type-safe filters using C# expressions — field names derived from generated bindings
- Parameterized SQL queries — no SQL injection by construction
- Composable `Filter.Or` / `Filter.And` combinators
- OpenTelemetry tracing via `ActivitySource`

### Client kernel (`Canton.Ledger.Kernel`)
- `Authentication`: OAuth2 client-credentials flow with thread-safe TTL token caching and automatic refresh
- Static token and unauthenticated modes behind a single `ITokenProvider` abstraction
- `IServiceCollection` integration with options validation at startup
- `Telemetry`: the shared `ActivitySource` naming convention; `Resilience`: the opt-in Polly retry pipeline

## Ledger Client Usage

The clients are entered through dependency injection: register them on an `IServiceCollection`, then
resolve the transport-neutral `ICantonLedgerClient` and `IAdminClient`. The container owns the gRPC
channel and the client lifetime, binds and validates `LedgerClientOptions` at startup, and injects
whichever `ITokenProvider` is registered.

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
services.AddAdminClient(options => options.GrpcAddress = "https://localhost:5001");

await using var provider = services.BuildServiceProvider();
var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
var adminClient = provider.GetRequiredService<IAdminClient>();

var party = await adminClient.AllocatePartyAsync("alice");
var submitter = party.Party;

var outcome = await ledgerClient.TryCreateAsync(
    new MyTemplate("field1", "field2"),
    submitter);

var contractId = outcome switch
{
    ExerciseOutcome<ContractId<MyTemplate>>.One ok => ok.Result,
    ExerciseOutcome<ContractId<MyTemplate>>.DamlError err => throw new InvalidOperationException(err.ErrorId),
    _ => throw new InvalidOperationException(outcome.GetType().Name),
};
```

`TryCreateAsync(payload, submitter)` is the client's own method and works for any template;
the generated `TryCreateAsync(payload)` overload from the [Quick Start](../../README.md#quick-start) derives
the submitter for you. A host that reads its settings from configuration can register both
clients and their authentication in one call with `services.AddCantonLedger(configuration)`,
which reads the `Canton:Ledger` and `Canton:Auth` sections; with no `ITokenProvider` registered
the clients run unauthenticated, which suits a local participant with open access.

## Handling Failures

The gRPC and JSON Ledger API clients report failure the same way, so code written against `ICantonLedgerClient` handles both without a transport-specific `catch`:

- A throwing call raises `LedgerOperationException` carrying the participant's `Category`, `ErrorId` and `Metadata`, the transport's `Status` and the original exception as `InnerException`. It is never `RpcException`, `HttpRequestException` or `TaskCanceledException`.
- A `Try*` call returns an `ExerciseOutcome<T>`; a stream ends with a terminal `StreamError`.
- Only your own `CancellationToken` surfaces as `OperationCanceledException`.
- A failing `ITokenProvider` is outside that contract on gRPC: its exception propagates unchanged, so catch your provider's own exception types there. Over the JSON Ledger API an `HttpRequestException`, `TimeoutException` or caller-uncaused `OperationCanceledException` from the provider becomes a `LedgerOperationException` with `TransportStatus.NoResponse`, and any other provider exception propagates unchanged.

`CommitState` says whether retrying is safe: `NotCommitted` for a failed read and for most rejected writes, `Committed` when the ledger already applied the command (a `DUPLICATE_COMMAND` rejection, unless its `accepted` metadata is `"false"`), and `Unknown` when a write may have reached the participant without an answer, including a `SUBMISSION_ALREADY_IN_FLIGHT` rejection and one whose category leaves the request state unknown.

```csharp
try
{
    await ledgerClient.ExerciseAsync(command, owner);
}
catch (LedgerOperationException ex) when (ex.CommitState == CommitState.NotCommitted)
{
    Console.WriteLine($"Rejected with {ex.Category}; safe to retry");
}
```

## Reading a Disclosure

`GetDisclosureAsync<T>(contractId, submitter)` returns the data that discloses a contract explicitly, or `null` when the submitter cannot see it, it does not exist or it is archived. `T` may be a template or an interface:

```csharp
var disclosure = await ledgerClient.GetDisclosureAsync(contractId, owner);

if (disclosure is not null)
{
    var submission = baseSubmission.WithDisclosedContracts(disclosure);
}
```

## PQS Client Usage

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Contracts;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddPqsClient(options =>
    options.ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs");

await using var provider = services.BuildServiceProvider();
var pqsClient = provider.GetRequiredService<IPqsClient>();

// Query all active contracts of a template type
var agreements = await pqsClient.QueryAsync<Agreement>();

// Query with type-safe filters — field names resolve from codegen [DamlField] metadata
var partyId = "party::alice";
var filtered = await pqsClient.QueryAsync<Agreement>(
    Filter.Or(
        Filter.Field<Agreement>(a => a.Initiator, partyId),
        Filter.Field<Agreement>(a => a.Counterparty, partyId)));

// Fetch a single contract by ID
var contractId = new ContractId<Agreement>("...");
var contract = await pqsClient.FetchByIdAsync<Agreement>(contractId);

// Check if a contract exists
var exists = await pqsClient.ExistsAsync<Agreement>(contractId);
```

`Filter.Where<T>(predicate)` accepts a richer C# predicate — comparisons, nested records,
`Optional`, list and map members, variants; the
[`Canton.Ledger.Pqs.Client` README](../../src/Canton.Ledger.Pqs.Client/README.md) lists what it
translates.

## Using Generated Types with the Clients

Templates generated by `dpm codegen-cs` (see [Generate Code](codegen.md#generate-code)) plug straight into the clients:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Pqs.Client;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLedgerClient(options => options.GrpcAddress = "https://localhost:5001");
services.AddPqsClient(options =>
    options.ConnectionString = "Host=localhost;Database=pqs;Username=pqs;Password=pqs");

await using var provider = services.BuildServiceProvider();
var ledgerClient = provider.GetRequiredService<ICantonLedgerClient>();
var pqsClient = provider.GetRequiredService<IPqsClient>();

var owner = new Party("Alice::1234...");

// Create a contract from a generated template type
var asset = new Asset(owner, "My Asset", 100m);
var createOutcome = await ledgerClient.TryCreateAsync(asset, owner);
var contractId = createOutcome switch
{
    ExerciseOutcome<ContractId<Asset>>.One ok => ok.Result,
    _ => throw new InvalidOperationException(createOutcome.GetType().Name),
};

// Exercise a choice — ExerciseCommand.For takes the contract id, the choice name,
// and the encoded choice argument (a DamlValue, here a record via ToRecord()).
var command = ExerciseCommand.For(
    contractId,
    new ChoiceName("Transfer"),
    new Asset.Transfer(NewOwner: new Party("Bob::5678...")).ToRecord());

var exerciseOutcome = await ledgerClient.TryExerciseAsync<ContractId<Asset>>(command, owner);

// Query the same contracts via PQS
var assets = await pqsClient.QueryAsync<Asset>(
    Filter.Field<Asset>(a => a.Owner, owner.Value));
```

## Authentication

`Canton.Ledger.Kernel` ships as a dependency of `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client` and `Canton.Ledger.Pqs.Client`. Register an authentication provider explicitly; the built-in providers may be added before or after unauthenticated `AddLedgerClient` or `AddRestLedgerClient` registrations:

```csharp
using Canton.Ledger.Kernel.Authentication;

// OAuth2 client-credentials with automatic refresh and caching
services.AddCantonAuth(configuration.GetSection("Canton:Auth"));

// ...or a fixed token for short-lived processes
services.AddCantonStaticAuth("eyJ...");
```

```json
{
  "Canton": {
    "Auth": {
      "Domain": "my-tenant.eu.auth0.com",
      "ClientId": "my-client-id",
      "ClientSecret": "my-client-secret",
      "Audience": "https://canton.network/"
    }
  }
}
```

When no `ITokenProvider` is registered, the clients run unauthenticated (`ITokenProvider.None`) and log a warning at construction. See the [`Canton.Ledger.Kernel` README](../../src/Canton.Ledger.Kernel/README.md) for the full options reference, including custom token endpoints (e.g. Keycloak).
