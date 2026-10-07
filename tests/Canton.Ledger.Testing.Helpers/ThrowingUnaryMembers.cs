// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Names the members of a ledger or admin client interface whose failure contract a response-decoding
/// sweep must cover: every unary <see cref="Task"/>-returning member that throws, which excludes the
/// <c>Try*</c> members because they report a failure as an outcome instead of throwing.
/// </summary>
public static class ThrowingUnaryMembers
{
    /// <summary>
    /// The throwing members that drain a stream rather than decode one unary response, so no unary
    /// response can be malformed for them.
    /// </summary>
    public static IReadOnlyList<string> StreamDrained { get; } = ["QueryActiveAsync(4)"];

    /// <summary>
    /// The keys of the throwing unary members of <paramref name="clientInterface"/> and the interfaces
    /// it inherits, each written as the member name followed by its parameter count in parentheses.
    /// </summary>
    public static IReadOnlyCollection<string> KeysOf(Type clientInterface) =>
        new[] { clientInterface }.Concat(clientInterface.GetInterfaces())
            .SelectMany(type => type.GetMethods())
            .Where(method => !method.Name.StartsWith("Try", StringComparison.Ordinal))
            .Where(method => method.ReturnType == typeof(Task)
                || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
            .Select(method => $"{method.Name}({method.GetParameters().Length})")
            .ToHashSet();
}
