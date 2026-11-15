// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Kernel.Telemetry;

internal static class ExceptionRedactingLoggerExtensions
{
    internal static ILogger RedactingExceptionsUnless(this ILogger logger, bool emit) =>
        emit || logger is ExceptionRedactingLogger ? logger : new ExceptionRedactingLogger(logger);
}

internal sealed class ExceptionRedactingLogger(ILogger inner) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        inner.Log(
            logLevel,
            eventId,
            state,
            exception is null ? null : new RedactedException(exception),
            formatter);
}
