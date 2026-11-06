# Splice.Api.Token.Registry.Client

Part of the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk). A typed HTTP client for the off-ledger API every Splice Token Standard registry serves: the metadata, transfer instruction, allocation and allocation instruction endpoints, generated with [Refitter](https://github.com/christianhelle/refitter) from the OpenAPI specs the Splice release publishes.

## Installation

```bash
dotnet add package Splice.Api.Token.Registry.Client
```

The package version is `<Splice M.m.p>.<g>`: the Splice release the specs come from, then the codegen generation, the same ordinal the `Splice.*` binding packages of that release carry. It depends on `Daml.Runtime` at the SDK version that built it.

## Usage

```csharp
using Splice.Api.Token.Registry.Client;
using Splice.Api.Token.Registry.Client.AllocationV2;

services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"));

var factory = await allocationApi.GetSettlementFactory(
    new GetFactoryRequest { ChoiceArguments = choiceArguments });

var disclosedContracts = factory.ChoiceContext.DisclosedContracts
    .Select(contract => contract.ToDisclosedContract())
    .ToList();
```

`AddSpliceTokenRegistryClients` registers one interface per API, over one named `HttpClient` (`ServiceCollectionExtensions.HttpClientName`) whose base address is the registry's. Outside dependency injection, build an interface with `RestService.For<T>(httpClient, SpliceRegistryRefitSettings.Create())`.

## What is covered

One namespace per OpenAPI spec of the Splice release, each with its own `I...Api` interface and its own copy of the shared schemas:

| Namespace | Interface | Routes |
|---|---|---|
| `TokenMetadataV1` | `ITokenMetadataV1Api` | `/registry/metadata/v1/...` |
| `TransferInstructionV1`, `TransferInstructionV2` | `ITransferInstructionV1Api`, `ITransferInstructionV2Api` | `/registry/transfer-instruction/{v1,v2}/...` |
| `AllocationV1`, `AllocationV2` | `IAllocationV1Api`, `IAllocationV2Api` | `/registry/allocations/v1/...`, `/registry/allocation/v2/...`, `/registry/allocations/v2/...` |
| `AllocationInstructionV1`, `AllocationInstructionV2` | `IAllocationInstructionV1Api`, `IAllocationInstructionV2Api` | `/registry/allocation-instruction/{v1,v2}/...` |

A choice context's `choiceContextData` is the registry's JSON `AnyValue` map, surfaced as the `object` Refitter generates for an untyped schema: at runtime a `System.Text.Json.JsonElement`.

`ToDisclosedContract()` turns the disclosed contracts of an allocation instruction V2 or allocation V2 choice context into `Daml.Runtime.Commands.DisclosedContract` for a submission. It throws `FormatException` for a `createdEventBlob` that is empty or not base64.

Non-success responses surface as `Refit.ApiException` carrying the HTTP status.

## Regeneration

The files under `Generated/` are committed and never edited by hand. They are produced from the OpenAPI tarball of the pinned Splice release, whose SHA-256 the repository pins next to the Splice version, and a drift check fails when the committed files differ from a regeneration.
