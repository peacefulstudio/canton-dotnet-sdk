# Talking to the Ledger API directly

How the runtime types map onto the JSON and gRPC Ledger API wire formats when you skip the high-level clients.

Back to the [README](../../README.md).

The highest-level path is the generated extension methods
(`IouSubmissionExtensions.TryCreateAsync`, `IouExtensions.TryTransferAsync`, …)
submitting through a `Daml.Ledger.Abstractions.ILedgerClient`
implementation: `Canton.Ledger.Grpc.Client` (gRPC), `Canton.Ledger.Rest.Client` (JSON
Ledger API), or — for unit-testing application code without a live
participant — the `Canton.Ledger.Testing` in-memory fakes. Below that, the runtime types
map directly onto the wire formats.

## JSON Ledger API (v2)

The generated `TemplateId` and `DamlJsonSerializer` output plug straight
into the JSON Ledger API v2 command endpoints — here
`POST /v2/commands/submit-and-wait` (the port is the participant's JSON
Ledger API port; `7575` is the conventional default):

```csharp
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using IouContract = Iou.Iou;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:7575") };

var alice = new Party("Alice::1220deadbeef");
var iou = new IouContract(Issuer: alice, Owner: new Party("Bob::1220deadbeef"), Currency: "USD", Amount: 100m);

var request = new
{
    commands = new object[]
    {
        new
        {
            CreateCommand = new
            {
                templateId = IouContract.TemplateId.ToString(),
                createArguments = JsonNode.Parse(DamlJsonSerializer.Serialize(iou.ToRecord())),
            },
        },
    },
    commandId = Guid.NewGuid().ToString(),
    userId = "ledger-api-user",
    actAs = new[] { alice.Value },
};

var response = await http.PostAsJsonAsync("/v2/commands/submit-and-wait", request);
response.EnsureSuccessStatusCode();
```

`IouContract.TemplateId.ToString()` renders the package-id reference format
(`<package-id>:<module>:<entity>`); the API also accepts the
package-name format (`#<package-name>:<module>:<entity>`), which can be
built from the generated `PackageName` property.

## gRPC Ledger API

The runtime types can be converted to/from the gRPC protobuf types with
`Daml.Runtime.Grpc`; `Canton.Ledger.Grpc.Client` packages that conversion behind
`ILedgerClient` so most applications never touch the protos, and `Canton.Ledger.Grpc`
exposes the raw stubs for the rest. See the
[Canton documentation](https://docs.canton.network/) for raw gRPC
integration details.
