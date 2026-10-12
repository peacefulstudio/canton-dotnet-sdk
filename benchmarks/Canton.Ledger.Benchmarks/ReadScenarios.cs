// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;

namespace Canton.Ledger.Benchmarks;

internal static class ReadScenarios
{
    public static async Task<ThroughputSummary> UpdateStreamReplayAsync(
        Transport transport,
        Party owner,
        LedgerOffset fromOffset,
        LedgerOffset toOffset,
        int expectedCreates,
        ReadSampling sampling,
        CancellationToken cancellationToken)
    {
        var (elapsed, allocated) = await WarmedRepetitions.MeasureAsync(
                sampling,
                async token =>
                {
                    var creates = 0;
                    await foreach (var streamEvent in transport.Client
                                       .SubscribeAsync<Marker>(owner, fromOffset, toOffset, token)
                                       .ConfigureAwait(false))
                    {
                        switch (streamEvent)
                        {
                            case ContractStreamEvent<Marker>.Created:
                                creates++;
                                break;
                            case ContractStreamEvent<Marker>.StreamError error:
                                throw new InvalidOperationException(
                                    $"{transport.Name} update stream failed: {error.Status} {error.ErrorId} {error.Message}");
                        }
                    }

                    ExpectCount(transport, "update stream replay", expectedCreates, creates);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ThroughputSummary.Of(expectedCreates, elapsed, allocated);
    }

    public static async Task<ThroughputSummary> ActiveContractSnapshotAsync(
        Transport transport,
        Party owner,
        LedgerOffset activeAtOffset,
        int expectedContracts,
        ReadSampling sampling,
        CancellationToken cancellationToken)
    {
        var (elapsed, allocated) = await WarmedRepetitions.MeasureAsync(
                sampling,
                async token =>
                {
                    var contracts = 0;
                    await foreach (var entry in transport.Client
                                       .SubscribeActiveAsync<Marker>(owner, activeAtOffset, cancellationToken: token)
                                       .ConfigureAwait(false))
                    {
                        switch (entry)
                        {
                            case AcsSnapshotEntry<Marker>.Created:
                                contracts++;
                                break;
                            case AcsSnapshotEntry<Marker>.StreamError error:
                                throw new InvalidOperationException(
                                    $"{transport.Name} ACS snapshot failed: {error.Status} {error.ErrorId} {error.Message}");
                        }
                    }

                    ExpectCount(transport, "ACS snapshot", expectedContracts, contracts);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return ThroughputSummary.Of(expectedContracts, elapsed, allocated);
    }

    private static void ExpectCount(Transport transport, string scenario, int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{transport.Name} {scenario} read {actual} contracts where {expected} were seeded");
        }
    }
}
