# dotnet new template

`Canton.ProjectTemplates` ships the `canton-app` template: a Daml contract, the C# bindings generated
from it, and a console app that creates the contract on a LocalNet participant and reads it back.
It is the smallest project that goes from Daml source to a running ledger call, and the one to copy
when you start your own.

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0.100 or later | building and running the projects |
| `dpm` and Daml SDK 3.7.1 | `dpm` on `PATH`, then `dpm install 3.7.1` | building the Daml project; no JDK is involved |
| Canton LocalNet | running locally | `dotnet run`, which talks to the `a-validator-1` participant |

`dotnet build` needs no ledger. Only `dotnet run` does.

## Use it

```bash
dotnet new install Canton.ProjectTemplates
dotnet new canton-app --name MyCantonApp
cd MyCantonApp
dotnet build
dotnet run --project App
```

`--name` becomes the solution name and the namespace prefix, so use a valid C# namespace such as
`Acme.Ledger`. A successful run prints the created contract id and the greeting read back from the
active contract set:

```text
Created 00b864bf2648dd60...
author-9d9050c6e0e243f7a6776742e1cad10a::1220c1ac... says: Hello from Canton
```

Each run allocates a new party, so repeated runs do not collide.

## What each file does

| File | Role |
|---|---|
| `MyCantonApp.slnx` | The solution that holds both projects, so `dotnet build` works from the root. |
| `global.json` | Pins the .NET 10 SDK and lets a newer SDK roll forward. |
| `Directory.Packages.props` | Central package versions. Every SDK package is pinned to the version of the template package you installed, so upgrade the template and the pins move together. |
| `daml/daml.yaml` | The Daml package: SDK version, name and Daml-LF target. |
| `daml/HelloWorld.daml` | One `Greeting` template with an `author` and a `message`. |
| `Contracts/Contracts.csproj` | Builds the Daml project and generates C# bindings at `dotnet build` time. |
| `App/App.csproj` | The console app, referencing `Contracts` and `Canton.Ledger.Grpc.Client`. |
| `App/Program.cs` | Connects, uploads the DAR, allocates a party, creates a `Greeting` and queries it back. |
| `App/appsettings.json` | The connection and authentication settings for a stock LocalNet. |

## How the contract becomes C#

`Contracts.csproj` declares the Daml project with one item:

```xml
<DamlProject Include="../daml" />
```

`Daml.Codegen.CSharp.MSBuild` runs `dpm build` on it, then generates bindings in its default
`Standalone` mode, which needs no JDK. The `HelloWorld` module becomes the
`MyCantonApp.Contracts.HelloWorld` namespace, set by `<DamlNamespace>`, and the template becomes the
`Greeting` record. A small target copies the built DAR next to the assembly so the app can upload it.
See [codegen.md](codegen.md) for the emitted shapes and [type-mappings.md](type-mappings.md) for how
Daml types map to C#.

## What the app does

1. Reads `appsettings.json`, then environment variables, which override it.
2. Registers the clients with `AddCantonLedger`, which reads the `Canton:Ledger` and `Canton:Auth` sections.
3. Uploads the DAR and allocates a party through `IAdminClient`.
4. Grants the configured ledger user the right to act as that party. LocalNet only: a real deployment
   provisions its users and rights outside the application.
5. Creates a `Greeting` with `ICantonLedgerClient.TryCreateAsync` and unwraps the single outcome.
6. Reads the active contracts back with `SnapshotAsync<Greeting>` and prints each one.

## Connection settings

`appsettings.json` carries the stock `a-validator-1` LocalNet values: the gRPC address
`http://localhost:11901`, the Keycloak token endpoint for the `AValidator1` realm, the
`a-validator-1-validator` client, and that validator's service-account user id in
`Canton:Ledger:UserId`. The client secret in the file is the public demo credential every LocalNet
ships with, valid only against a local Keycloak.

To point the app elsewhere, override any key with an environment variable, using `__` for the section
separator:

```bash
Canton__Ledger__GrpcAddress=https://participant.example.com:443 dotnet run --project App
```

Every client option and its default is in [configuration-reference.md](configuration-reference.md);
the clients themselves are in [ledger-clients.md](ledger-clients.md).

## Next steps

- Add fields, templates and choices to `daml/HelloWorld.daml` and rebuild; the bindings regenerate.
- Add a second module to the same Daml project; each module becomes its own namespace.
- Swap `Canton.Ledger.Grpc.Client` for `Canton.Ledger.Rest.Client` to use the JSON Ledger API.
- Read the [mini demo](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo) for a Token
  Standard V2 transfer across both transports.
