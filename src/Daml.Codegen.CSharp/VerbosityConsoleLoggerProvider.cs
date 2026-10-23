// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes single-line, severity-prefixed records to the
/// console: errors and warnings to <see cref="Console.Error"/>, informational and debug records to
/// <see cref="Console.Out"/>. Intended for the command-line entry points; library code takes an
/// <see cref="ILogger"/> and defaults to <c>NullLogger</c>, so nothing is written unless a host
/// opts in.
/// </summary>
/// <remarks>
/// Writes synchronously rather than through a background queue, so a caller that redirects the
/// console streams sees every record the moment the logging call returns. Verbosity maps onto
/// <see cref="LogLevel"/> as 0 = errors only, 1 = warnings, 2 = information, 3 = debug.
/// </remarks>
internal sealed class VerbosityConsoleLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimumLevel;

    /// <summary>Creates a provider gated at the level implied by <paramref name="verbosity"/>.</summary>
    /// <param name="verbosity">0 = errors only, 1 = warnings, 2 = information, 3 or more = debug.</param>
    public VerbosityConsoleLoggerProvider(int verbosity) =>
        _minimumLevel = verbosity switch
        {
            <= 0 => LogLevel.Error,
            1 => LogLevel.Warning,
            2 => LogLevel.Information,
            _ => LogLevel.Debug,
        };

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new VerbosityConsoleLogger(_minimumLevel);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class VerbosityConsoleLogger(LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            var writer = logLevel >= LogLevel.Warning ? Console.Error : Console.Out;
            writer.WriteLine($"{Prefix(logLevel)}: {message}");
        }

        private static string Prefix(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Critical or LogLevel.Error => "ERROR",
            LogLevel.Warning => "WARN",
            LogLevel.Information => "INFO",
            _ => "DEBUG",
        };
    }
}
