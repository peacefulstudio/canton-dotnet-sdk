// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Kernel.Telemetry;

internal static class ExceptionRedactingLoggerExtensions
{
    internal static ILogger RedactingExceptionsUnless(this ILogger logger, bool emit) =>
        emit || logger is ExceptionRedactingLogger ? logger : new ExceptionRedactingLogger(logger);

    internal static Exception RedactingUnless(this Exception exception, bool emit) =>
        emit || exception is ExceptionRedactingLogger.RedactedException ? exception : new ExceptionRedactingLogger.RedactedException(exception);
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

    internal sealed class RedactedException(Exception original) : Exception(TypeNameOf(original))
    {
        public override string? StackTrace => original.StackTrace;

        public override string ToString()
        {
            var text = new StringBuilder();
            for (var current = original; current is not null; current = current.InnerException)
            {
                text.Append(text.Length == 0 ? string.Empty : " ---> ")
                    .Append(TypeNameOf(current))
                    .AppendLine()
                    .AppendLine(current.StackTrace);
            }

            return text.ToString();
        }

        private static string TypeNameOf(Exception exception) =>
            exception.GetType().FullName ?? exception.GetType().Name;
    }
}
