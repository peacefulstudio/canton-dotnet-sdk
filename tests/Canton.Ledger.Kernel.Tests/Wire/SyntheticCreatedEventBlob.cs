// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using LfTransaction = Com.Digitalasset.Daml.Lf.Transaction;
using LfValue = Com.Digitalasset.Daml.Lf.Value;

namespace Canton.Ledger.Kernel.Tests.Wire;

internal static class SyntheticCreatedEventBlob
{
    internal static LfTransaction.FatContractInstance Contract() => new()
    {
        ContractId = ByteString.CopyFrom(0x00, 0xab, 0xcd),
        PackageName = "tokens",
        TemplateId = new LfValue.Identifier
        {
            PackageId = "pkg1",
            ModuleName = { "Tokens", "Holding" },
            Name = { "Holding" },
        },
        CreateArg = ByteString.CopyFrom(0x6a, 0x00),
        NonMaintainerSignatories = { "admin::1" },
        CreatedAt = 1_000_000,
    };

    internal static byte[] InVersion(string version, LfTransaction.FatContractInstance contract) =>
        new LfTransaction.Versioned { Version = version, Payload = contract.ToByteString() }.ToByteArray();
}
