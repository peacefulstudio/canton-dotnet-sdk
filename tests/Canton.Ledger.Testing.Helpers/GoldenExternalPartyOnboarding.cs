// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// The single PartyToParticipant onboarding transaction Canton 3.6.1 generates for party hint
/// <c>alice</c> and an Ed25519 key, so an allocation under test names a party the SDK can derive.
/// </summary>
public static class GoldenExternalPartyOnboarding
{
    /// <summary>The party the golden transaction onboards: the hint in the key's namespace.</summary>
    public static readonly Party Party =
        new("alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa");

    /// <summary>The versioned, serialized topology transaction.</summary>
    public static byte[] Transaction() => Convert.FromHexString(
        "0ab701080110011ab0014aad010a4b616c6963653a3a313232303230353035376533333163633839323964643231376532663865"
        + "363366353033623730383137373364653630643031666234363833393730306263356361616110011a1f0a1b5041523a3a70617274"
        + "69636970616e74313a3a31323230616161611002323b0a3710041a2c302a300506032b6570032100d91c148881d551576e971cc3f0"
        + "d2ad3e621a681a8c7abd40f74ac38e892524f62a0301050430011001101e");

    /// <summary>The transaction as an unsigned onboarding transaction.</summary>
    public static SignedTopologyTransaction UnsignedTransaction() => new(Transaction(), []);
}
