// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;

namespace Canton.Ledger.Kernel.Tests.Wire;

internal static class CapturedV31Topology
{
    private const string FixturePath = "Fixtures/ExternalPartyTopology/v31-protocol-version-37.txt";

    private static readonly Dictionary<string, string> Lines = File.ReadAllLines(
            Path.Combine(AppContext.BaseDirectory, FixturePath))
        .Select(line => line.Split('=', 2))
        .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    internal static byte[] Transaction { get; } = Convert.FromHexString(Lines["topology-transaction"]);

    internal static byte[] MultiHash { get; } = Convert.FromHexString(Lines["multi-hash"]);

    internal static string PublicKeyFingerprint => Lines["public-key-fingerprint"];

    internal static Party Party => new(Lines["party"]);

    internal static ExternalPartyTopologyRequest Request() =>
        new(
            new SynchronizerId(Lines["synchronizer"]),
            Lines["party-id-hint"],
            new SigningPublicKey(
                PublicKeyFormat.DerX509SubjectPublicKeyInfo,
                Convert.FromHexString(Lines["public-key-spki"]),
                SigningKeySpec.EcP256));

    internal static ExternalPartyTopology Topology() =>
        new(Party, PublicKeyFingerprint, [Transaction], MultiHash);
}
