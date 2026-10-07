// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.Tests.TestHelpers;

/// <summary>
/// An <see cref="ILogger"/> that keeps every record in memory, so a test can assert on the
/// severity and the formatted text the emitter produced.
/// </summary>
public sealed class CapturingLogger : ILogger
{
    /// <summary>Every record logged so far, in order.</summary>
    public List<(LogLevel Level, string Message)> Records { get; } = [];

    /// <summary>The formatted text of every warning logged so far.</summary>
    public IEnumerable<string> Warnings =>
        Records.Where(r => r.Level == LogLevel.Warning).Select(r => r.Message);

    /// <summary>The event id of every warning logged so far.</summary>
    public IEnumerable<int> WarningEventIds => _warningEventIds;

    private readonly List<int> _warningEventIds = [];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Records.Add((logLevel, formatter(state, exception)));
        if (logLevel == LogLevel.Warning)
        {
            _warningEventIds.Add(eventId.Id);
        }
    }
}
