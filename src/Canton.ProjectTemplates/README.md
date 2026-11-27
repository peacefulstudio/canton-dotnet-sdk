# Canton.ProjectTemplates

`dotnet new` templates for the [Canton .NET SDK](https://github.com/peacefulstudio/canton-dotnet-sdk).

## Install

```bash
dotnet new install Canton.ProjectTemplates
```

## Templates

| Short name | What it creates |
| --- | --- |
| `canton-app` | A Daml contract, its generated C# bindings and a console app that creates the contract on a LocalNet participant and reads it back. |

```bash
dotnet new canton-app --name MyCantonApp
cd MyCantonApp
dotnet run --project App
```

The generated projects reference the SDK packages at the version of the template package you installed. Building needs the .NET 10 SDK or later and `dpm` on `PATH` with Daml SDK 3.7.1 installed (`dpm install 3.7.1`); no JDK is involved.

See the [template guide](https://github.com/peacefulstudio/canton-dotnet-sdk/blob/main/docs/public/dotnet-new-template.md) for what each generated file does.
