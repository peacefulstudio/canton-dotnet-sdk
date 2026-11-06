// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Canton.Ledger.AspNetCore;

/// <summary>Declares the ledger failure responses of a minimal-API endpoint in its OpenAPI metadata.</summary>
public static class LedgerProblemEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Declares every response a <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> can be
    /// answered with: <c>202</c> as a <see cref="CommittedCommandProblem"/> (the command committed; the
    /// caller must not retry), <c>400</c>, <c>403</c>, <c>404</c>, <c>409</c> and <c>502</c> as
    /// <c>ProblemDetails</c>. <c>404</c> means a resource other than a contract is missing (a user, a DAR,
    /// an identity provider config); a contract that is gone (<c>CONTRACT_NOT_FOUND</c>) is <c>409</c>. Applies to a minimal-API endpoint or route group; an MVC controller declares
    /// the same responses with <see cref="ProducesLedgerProblemsAttribute"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or group to declare the responses on.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder ProducesLedgerProblems<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var (statusCode, responseType) in LedgerProblemResponses.All)
            builder.WithMetadata(new ProducesResponseTypeMetadata(statusCode, responseType, [LedgerProblemResponses.ProblemJson]));
        return builder;
    }
}
