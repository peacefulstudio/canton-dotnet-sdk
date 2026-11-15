// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Canton.Ledger.Abstractions;

internal static class RedactedExceptionExtensions
{
    internal static Exception RedactingUnless(this Exception exception, bool emit) =>
        emit || exception is RedactedException ? exception : new RedactedException(exception);
}

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
