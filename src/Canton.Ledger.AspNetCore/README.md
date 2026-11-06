# Canton.Ledger.AspNetCore

ASP.NET Core hosting for the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). One registration turns a `LedgerOperationException` thrown from a controller or a minimal-API handler into an RFC 9457 `ProblemDetails` response, so an endpoint writes `(await request.TryAcceptAsync(client, argument, submitter)).OneOrThrow()` and no catch block.

It works with both MVC controllers and minimal APIs. It does not register the ledger client: `AddLedgerClient` (`Canton.Ledger.Grpc.Client`) and `AddRestLedgerClient` (`Canton.Ledger.Rest.Client`) already do.

## Installation

```bash
dotnet add package Canton.Ledger.AspNetCore --version 0.8.0
```

## Key Types

| Type | Purpose |
|------|---------|
| `AddCantonLedgerProblemDetails(this IServiceCollection, Action<LedgerProblemOptions>?)` | Registers `AddProblemDetails()` and the `IExceptionHandler` that maps `LedgerOperationException`. |
| `LedgerProblemOptions` | `EmitErrorMetadataSpanTags` (default `false`) copies the Daml error metadata onto the request's span as `daml.error_metadata.<key>` tags. Metadata can name parties and contracts, so it stays off unless you opt in. |
| `ProducesLedgerProblems()` | Declares the failure responses in the OpenAPI metadata of a minimal-API endpoint or route group. |
| `[ProducesLedgerProblems]` | The same declaration for an MVC controller or action. |
| `CommittedCommandProblem` | The `ProblemDetails` shape of the `202` response, with `commandId` and `updateId`. |

## Usage

```csharp
builder.Services.AddCantonLedgerProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();

app.MapPost("/connection-requests/{id}/accept", AcceptAsync).ProducesLedgerProblems();
```

An MVC controller carries `[ProducesLedgerProblems]` instead.

## Responses

| Status | When | Extensions |
|--------|------|------------|
| `202` | The command committed but the handler could not finish: the result did not decode, or the outcome held no or several results. | `commandId`, `updateId` |
| `400` | The ledger rejected the command as invalid (`InvalidIndependentOfSystemState`, `InvalidGivenCurrentSystemStateOther`, `SecurityAlert`), or a redacted refusal that carries only gRPC `InvalidArgument` or HTTP `400`. The detail is the exception message. | |
| `403` | `InsufficientPermission`, or a redacted refusal that carries only gRPC `PermissionDenied` or HTTP `403`. | |
| `404` | A missing resource other than a contract (`ResourceMissing`). | |
| `409` | Contention, an existing resource, or a contract that is gone (`CONTRACT_NOT_FOUND`). | |
| `502` | The ledger is unavailable or failed, or never confirmed whether the command committed (title `Outcome Unknown`, with `commandId`). | |

A `202` problem means the command is on the ledger. Re-read the resource and never resubmit.

Problem details never carry Daml error metadata, so a response does not leak party or contract ids. Each failure is logged with the operation name (`LedgerOperation.Label`, such as `ConnectionRequest.Accept`), the commit state and the error id, at warning below 500 and at error from 500. A client that sends `Accept: text/html` still receives `application/problem+json`.
