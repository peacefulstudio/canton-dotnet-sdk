// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Benchmarks;

internal sealed record ReadSampling(TimeSpan Warmup, int MinimumRepetitions, TimeSpan TimedWindow)
{
    public static ReadSampling From(BenchmarkOptions options) =>
        new(
            TimeSpan.FromMilliseconds(options.ReadWarmupMs),
            options.Repetitions,
            TimeSpan.FromMilliseconds(options.ReadWindowMs));
}
