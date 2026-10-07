# Status, platform support and roadmap

Where the SDK stands: release status, the OS and architecture CI covers, test coverage and what is planned next.

Back to the [README](../../README.md).

## Status

Active. Curated public releases land here; ongoing development happens
in a private development repository. Issues, discussions, and pull
requests are welcome on this repo.

This project is pre-1.0: under SemVer 0.x, any release may change the
public API without a major-version bump (see the versioning note at the
top of the [CHANGELOG](../../CHANGELOG.md)). The first release published to
NuGet.org is `0.1.8-preview.1`.

## Platform support

CI builds and tests on every supported OS × architecture. Each badge reflects the latest `main` run.

**.NET — every package in `Daml.Codegen.CSharp.slnx`**

| Ubuntu | Windows | macOS |
|---|---|---|
| [![ubuntu amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-ubuntu-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-windows-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-macos-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |
| [![ubuntu arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-ubuntu-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-windows-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-csharp-macos-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |

**JVM helper (`jvm-helper/`, the DAR decoder inside `dpm codegen-cs`)**

| Ubuntu | Windows | macOS |
|---|---|---|
| [![ubuntu amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-ubuntu-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![windows amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-windows-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | [![macos amd64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-macos-amd64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |
| [![ubuntu arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-ubuntu-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) | ![windows arm64 not supported](https://img.shields.io/badge/arm64-not%20supported-lightgrey?logo=data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0id2hpdGUiIGQ9Ik0wIDMuNDQ5TDkuNzUgMi4xdjkuNDUxSDBtMTAuOTQ5LTkuNjAyTDI0IDB2MTEuNEgxMC45NDlNMCAxMi42aDkuNzV2OS40NTFMMCAyMC42OTlNMTAuOTQ5IDEyLjZIMjRWMjRsLTEyLjktMS44MDEiLz48L3N2Zz4=&logoColor=white) | [![macos arm64](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/ci-scala-macos-arm64.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml) |

## Coverage

[![coverage (C#)](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/coverage-csharp.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)
[![coverage (Scala)](https://img.shields.io/endpoint?url=https://raw.githubusercontent.com/peacefulstudio/canton-dotnet-sdk/badges/coverage-scala.json)](https://github.com/peacefulstudio/canton-dotnet-sdk/actions/workflows/ci.yaml)

## Roadmap

### Completed

- [x] IntermediateDar proto reader covering the full Daml-LF type surface
- [x] Interface support (`IDamlInterface`, `IHasView<TView>`, `IImplements<TInterface>`)
- [x] Contract key support (key type, `<Choice>ByKeyCommand` builders, and a non-nullable `Contract<T, TKey>.Key` decoded from the created event's `contractKey`)
- [x] Package upgrade support (`IUpgradeable` marker interface)
- [x] Generic types (type parameters on records and variants)
- [x] DAR dependencies and NuGet pipeline
- [x] gRPC, JSON Ledger API and PQS clients shipped in the SDK under one version
- [x] End-to-end Canton integration tests — generated conformance types round-tripped against a live participant over gRPC and the JSON Ledger API, plus a cross-transport parity suite

### Planned

- [ ] Source generator (compile-time Roslyn codegen)
