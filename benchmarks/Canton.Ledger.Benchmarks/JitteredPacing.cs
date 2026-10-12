// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Benchmarks;

internal sealed class JitteredPacing(TimeSpan minimum, TimeSpan jitter)
{
    private const int Seed = 1898;

    private readonly Random _random = new(Seed);

    public static JitteredPacing NoPause => new(TimeSpan.Zero, TimeSpan.Zero);

    public TimeSpan NextPause() => minimum + jitter * _random.NextDouble();

    public Task PauseAsync(CancellationToken cancellationToken)
    {
        var pause = NextPause();
        return pause > TimeSpan.Zero ? Task.Delay(pause, cancellationToken) : Task.CompletedTask;
    }
}
