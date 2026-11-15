// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Canton.Ledger.AspNetCore;

/// <summary>Adds the ledger failure mapping to the ASP.NET Core request pipeline.</summary>
public static class LedgerProblemApplicationBuilderExtensions
{
    /// <summary>
    /// Answers a <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> escaping the rest of the
    /// pipeline with the same RFC 9457 <c>application/problem+json</c> response, logging and span tagging
    /// that <c>UseExceptionHandler()</c> produces after <see cref="LedgerProblemServiceCollectionExtensions.AddCantonLedgerProblemDetails"/>,
    /// and lets every other exception propagate untouched to the middleware above it. Use it instead of
    /// <c>UseExceptionHandler()</c> when the host has its own catch-all exception handling, which
    /// <c>UseExceptionHandler()</c> would pre-empt by answering every exception itself. Place it inside
    /// request logging, so the logging sees the mapped status code rather than an exception, and inside the
    /// host's catch-all. When the response has already started, the exception propagates unanswered, as it
    /// does through ASP.NET Core's own exception handler.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="LedgerProblemServiceCollectionExtensions.AddCantonLedgerProblemDetails"/> was not called.
    /// </exception>
    public static IApplicationBuilder UseCantonLedgerProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<LedgerOperationExceptionHandler>() is null)
            throw new InvalidOperationException(
                "UseCantonLedgerProblemDetails() needs services.AddCantonLedgerProblemDetails() to be called first.");

        return app.UseMiddleware<LedgerOperationExceptionMiddleware>();
    }
}
