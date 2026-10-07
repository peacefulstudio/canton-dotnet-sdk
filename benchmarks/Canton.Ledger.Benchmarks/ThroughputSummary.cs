// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Benchmarks;

internal sealed record ThroughputSummary(int Items, int Repetitions, double MedianSeconds, double MedianItemsPerSecond, double BestItemsPerSecond, double? AllocatedBytesPerItem = null)
{
    public static ThroughputSummary Of(
        int items, IReadOnlyCollection<TimeSpan> repetitions, IReadOnlyCollection<long>? allocatedBytes = null)
    {
        if (repetitions.Count == 0)
        {
            throw new InvalidOperationException("a throughput summary needs at least one repetition");
        }

        var sortedSeconds = repetitions.Select(elapsed => elapsed.TotalSeconds).Order().ToArray();
        var medianSeconds = sortedSeconds[(sortedSeconds.Length - 1) / 2];
        return new ThroughputSummary(
            items,
            sortedSeconds.Length,
            medianSeconds,
            items / medianSeconds,
            items / sortedSeconds[0],
            allocatedBytes is { Count: > 0 } ? MedianBytes(allocatedBytes) / items : null);
    }

    private static double MedianBytes(IReadOnlyCollection<long> allocatedBytes)
    {
        var sorted = allocatedBytes.Order().ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }
}
