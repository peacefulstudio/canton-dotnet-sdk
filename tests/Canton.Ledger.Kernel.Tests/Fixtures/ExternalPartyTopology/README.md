# External-party topology fixtures

`v31-protocol-version-37.txt` holds the answer of a Canton 3.7.1 participant on a protocol version 37
synchronizer to `GenerateExternalPartyTopology`, byte for byte: the request it answered (synchronizer,
party id hint and the public key as a DER `SubjectPublicKeyInfo`, curve P-256) and the response (party,
public key fingerprint, the one topology transaction and the multi-hash), each as a `key=value` line
with the bytes in lower-case hex. The transaction is wrapped as topology proto version 31.

Nothing in it is edited. The participant UID inside the transaction is the LocalNet participant's.

## Capture

Boot the LocalNet that `peacefulstudio/canton-localnet` v0.10.0-1 starts (Splice 0.10.0, Canton 3.7.1;
its default protocol version is 37, where the participant returns `v31` topology transactions). Then
run, against the `a-validator-1` participant, the call below with a freshly generated P-256 key:

```csharp
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var publicKey = new SigningPublicKey(
    PublicKeyFormat.DerX509SubjectPublicKeyInfo, key.ExportSubjectPublicKeyInfo(), SigningKeySpec.EcP256);
var generated = await admin.GenerateExternalPartyTopologyAsync(
    new ExternalPartyTopologyRequest(synchronizer, partyIdHint, publicKey), cancellationToken);

Console.WriteLine(string.Join(Environment.NewLine,
[
    $"synchronizer={synchronizer.Value}",
    $"party-id-hint={partyIdHint}",
    $"public-key-spki={Convert.ToHexStringLower(publicKey.KeyData.Span)}",
    $"party={generated.Party.Value}",
    $"public-key-fingerprint={generated.PublicKeyFingerprint}",
    .. generated.TopologyTransactions.Select(transaction => $"topology-transaction={Convert.ToHexStringLower(transaction.Span)}"),
    $"multi-hash={Convert.ToHexStringLower(generated.MultiHash.Span)}",
]));
```

`admin` is the `IAdminClient` of either transport and `synchronizer` the id of the global synchronizer.
The call has to run on a client that decodes `v31`: the verifier of an earlier release refuses these
bytes, which is the failure the fixture pins.

The checked-in bytes come from the cross-transport parity suite's onboarding step
(`ExternalSigningSteps.OnboardAsync`), which generates the P-256 key and the hint, run on a throwaway CI
branch with the lines above added after its `GenerateExternalPartyTopologyAsync` call.

## Tests that read it

`ExternalPartyTopologyVerifierV31Tests` accepts it as is and derives each refusal case from it by
changing one field.
