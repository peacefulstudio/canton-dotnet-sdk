// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace Canton.Ledger.AspNetCore.Tests;

internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    internal IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
                return [.. _entries];
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries)
                owner._entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
