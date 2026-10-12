// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace Canton.Ledger.Benchmarks;

internal static class WarmedRepetitions
{
    public static async Task<(IReadOnlyList<TimeSpan> Elapsed, IReadOnlyList<long> AllocatedBytes)> MeasureAsync(
        ReadSampling sampling, Func<CancellationToken, Task> read, CancellationToken cancellationToken)
    {
        var warmupStart = Stopwatch.GetTimestamp();
        do
        {
            await read(cancellationToken).ConfigureAwait(false);
        }
        while (Stopwatch.GetElapsedTime(warmupStart) < sampling.Warmup);

        var elapsed = new List<TimeSpan>(sampling.MinimumRepetitions);
        var allocated = new List<long>(sampling.MinimumRepetitions);
        var timedStart = Stopwatch.GetTimestamp();
        while (elapsed.Count < sampling.MinimumRepetitions || Stopwatch.GetElapsedTime(timedStart) < sampling.TimedWindow)
        {
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            await read(cancellationToken).ConfigureAwait(false);
            elapsed.Add(Stopwatch.GetElapsedTime(start));
            allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);
        }

        return (elapsed, allocated);
    }
}
