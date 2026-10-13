// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using Canton.Ledger.Abstractions;
using Google.Protobuf;
using PreparedTransaction = Com.Daml.Ledger.Api.V2.Interactive.PreparedTransaction;

namespace Canton.Ledger.Kernel.Wire;

internal static class PreparedSubmissionIntegrity
{
    private const int HashLengthInBytes = 32;

    internal static PreparedSubmission RequireWellFormed(
        PreparedSubmission prepared, HashingSchemeVersion? requested)
    {
        if (prepared.Hash.Length != HashLengthInBytes)
        {
            throw MalformedResponse.WithDetail(
                $"the participant returned a prepared-transaction hash of {prepared.Hash.Length} bytes, though a hash is {HashLengthInBytes} bytes");
        }

        if (requested is { } scheme && prepared.HashingSchemeVersion != scheme)
        {
            throw MalformedResponse.WithDetail(
                $"the participant hashed the prepared transaction with scheme {prepared.HashingSchemeVersion}, though {scheme} was requested");
        }

        RequireHashOfTransaction(prepared);

        return prepared;
    }

    private static void RequireHashOfTransaction(PreparedSubmission prepared)
    {
        var scheme = prepared.HashingSchemeVersion;
        if (!PreparedTransactionHasher.Ports(scheme))
        {
            throw MalformedResponse.WithDetail(
                $"the participant hashed the prepared transaction with scheme {scheme}, which this SDK cannot recompute; "
                + "it verifies V2 and V3 only");
        }

        var recomputed = PreparedTransactionHasher.Hash(Decode(prepared.PreparedTransaction), scheme);
        if (!CryptographicOperations.FixedTimeEquals(recomputed, prepared.Hash.Span))
        {
            throw new PreparedHashMismatchException(scheme, prepared.Hash, recomputed);
        }
    }

    private static PreparedTransaction Decode(ReadOnlyMemory<byte> preparedTransaction)
    {
        try
        {
            return PreparedTransaction.Parser.ParseFrom(preparedTransaction.Span);
        }
        catch (InvalidProtocolBufferException undecodable)
        {
            throw MalformedResponse.WithDetail($"the prepared transaction could not be decoded: {undecodable.Message}", undecodable);
        }
    }
}
