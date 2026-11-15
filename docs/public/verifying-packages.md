# Verifying package signatures

Check that a Canton .NET SDK package you downloaded is the one nuget.org accepted from the `peaceful` account, and pin that check in a `nuget.config`.

Back to the [README](../../README.md).

## What is signed

nuget.org signs every package it accepts with its repository certificate. The signature covers the package contents and records the service index URL and the nuget.org account names that owned the package at upload. Every Canton .NET SDK package is published from the nuget.org account `peaceful`, so a package you trust carries `Owners: peaceful`.

The signer is nuget.org, not Peaceful Studio OÜ. The signature proves that nuget.org accepted the upload from the `peaceful` account and that the bytes have not changed since. It does not name the build that produced them.

You can confirm the owner name on the [`peaceful` profile](https://www.nuget.org/profiles/peaceful), which every package page links to under its owners.

## Verify a package

Download a package and run `dotnet nuget verify`. The commands below use `Canton.Ledger.Kernel` at `0.8.0`; substitute any package id and version.

```bash
curl -sSfLO https://api.nuget.org/v3-flatcontainer/canton.ledger.kernel/0.8.0/canton.ledger.kernel.0.8.0.nupkg
dotnet nuget verify --all -v detailed canton.ledger.kernel.0.8.0.nupkg
```

The package id and version in the URL are lowercase. The command exits `0` when every signature on the package verifies. This is the output of that run, trimmed to the lines that matter:

```text
Verifying Canton.Ledger.Kernel.0.8.0
Content hash: zlFpTQk0dMVrQtq93tEkQtxzNO/Uzkjxo0jt8SdqvQEQHzvsGfftnyZUPHIc+8Sm5LBkFeJDIRpBTyI0i1veDQ==
canton.ledger.kernel.0.8.0.nupkg
Signature Hash Algorithm: SHA256

Signature type: Repository
Service index: https://api.nuget.org/v3/index.json
Owners: peaceful
Verifying the repository primary signature with certificate:
  Subject Name: CN=NuGet.org Repository by Microsoft, O=NuGet.org Repository by Microsoft, L=Redmond, S=Washington, C=US
  SHA1 hash: C72FE7739A9EECB8EC1E4F596DB3BB74039B1DE2
  SHA256 hash: 1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D
  Issued by: CN=DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1, O="DigiCert, Inc.", C=US
  Valid from: 2/22/2024 9:00:00 PM to 5/18/2027 8:59:59 PM
Timestamp: 11/7/2026 11:26:13 AM
Verifying repository primary signature's timestamp with timestamping service certificate:
  Subject Name: CN=DigiCert SHA256 RSA4096 Timestamp Responder 2026 1, O="DigiCert, Inc.", C=US
  Issued by: CN=DigiCert Trusted G4 TimeStamping RSA4096 SHA256 2025 CA1, O="DigiCert, Inc.", C=US
  Valid from: 8/4/2026 9:00:00 PM to 11/4/2037 8:59:59 PM

Successfully verified package 'Canton.Ledger.Kernel.0.8.0'.
```

Two lines are the check: `Signature type: Repository` and `Owners: peaceful`. `dotnet nuget verify` prints dates in your machine's local time zone, so the certificate validity above, captured at UTC-3, reads three hours earlier than the UTC times in the next section.

## Pin the signer in nuget.config

A `trustedSigners` entry turns "some nuget.org signature" into "the nuget.org signature, on a package owned by `peaceful`". Put this in a `nuget.config` in the directory you run `dotnet nuget verify` from:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <trustedSigners>
    <repository name="nuget.org" serviceIndex="https://api.nuget.org/v3/index.json">
      <certificate fingerprint="1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D" hashAlgorithm="SHA256" allowUntrustedRoot="false" />
      <owners>peaceful</owners>
    </repository>
  </trustedSigners>
</configuration>
```

The same entry is one command:

```bash
dotnet nuget trust repository nuget.org canton.ledger.kernel.0.8.0.nupkg --owners peaceful
```

Add `--configfile <path>` to write somewhere other than your user-level config. With the entry in place, `dotnet nuget verify --all <package>.nupkg` succeeds for a package owned by `peaceful`. Run against every `Canton.Ledger.*` and `Daml.*` package at `0.8.0`, the pinned config passed each one.

Put the pinned config next to the packages you verify rather than in a restore-wide config. On restore, an entry like this applies to every package from nuget.org, including your dependencies, and each of those must also satisfy your policy. Restoring `Canton.Ledger.Kernel` under `signatureValidationMode` set to `require` with this entry failed `NU3034` on its dependencies, such as `Grpc.Net.Client` and `Microsoft.Extensions.Logging`, because their owners are not `peaceful`.

## The nuget.org certificate

| | |
|---|---|
| Subject | `CN=NuGet.org Repository by Microsoft` |
| SHA-256 fingerprint | `1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D` |
| Valid from | 2024-02-23 00:00:00 UTC |
| Valid until | 2027-05-18 23:59:59 UTC |

These values were read from nuget.org's repository signatures index and from the certificate it serves on 2026-11-07. The index is the source of truth, and the fingerprint and expiry here stop being current when nuget.org rotates:

```bash
curl -sS https://api.nuget.org/v3-index/repository-signatures/5.0.0/index.json
```

It lists every repository certificate nuget.org has used, with `notBefore`, `notAfter` and the SHA-256 fingerprint. On the capture date it listed three: 2018 to 2021, 2021 to 2024 and the current one above.

### When nuget.org rotates the certificate

nuget.org signs new uploads with the new certificate after a rotation. A config that pins only the old fingerprint then fails with `NU3034` on packages published after the rotation. Refresh your pin from the index:

```bash
dotnet nuget trust sync nuget.org
```

Add `--configfile <path>` to sync a config other than your user-level one. `trust sync` rewrites the repository entry from the index and keeps your `<owners>` list. Run it before the current certificate expires on 2027-05-18, and again whenever the index lists a certificate your config lacks. This run started from a config pinning only the 2021 to 2024 certificate (and failing `NU3034` against the `0.8.0` package), then synced:

```text
$ dotnet nuget trust sync nuget.org --configfile nuget.config
Successfully synchronized the trusted repository 'nuget.org'.
```

The entry afterwards listed all three fingerprints, still with `<owners>peaceful</owners>`, and `dotnet nuget verify --all` on the package exited `0`.

## NU3034

```text
error: NU3034: This package is signed but not by a trusted signer.

Package signature validation failed.
```

`NU3034` means the package's signature does not match any `trustedSigners` entry you configured. Two causes cover what this page sets up:

- **The owner does not match.** The `<owners>` list does not contain an owner of the package. A package owned by anyone other than `peaceful` fails against `<owners>peaceful</owners>`. Check the `Owners:` line in the verbose output against the owner you meant to pin. Running the pinned config with `<owners>someoneelse</owners>` against the `0.8.0` package produced the error above.
- **The fingerprint is not pinned.** nuget.org signed the package with a certificate your entry does not list, typically after a rotation. Run `dotnet nuget trust sync nuget.org`.
