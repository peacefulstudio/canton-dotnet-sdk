// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Canton.Ledger.AspNetCore;

/// <summary>
/// Declares, on an MVC controller or action, every response a
/// <see cref="Daml.Ledger.Abstractions.LedgerOperationException"/> can be answered with: <c>202</c> as a
/// <see cref="CommittedCommandProblem"/> (the command committed; the caller must not retry), <c>400</c>,
/// <c>403</c>, <c>404</c>, <c>409</c> and <c>502</c> as <c>ProblemDetails</c>. <c>404</c> means a resource
/// other than a contract is missing (a user, a DAR, an identity provider config); a contract that is gone
/// (<c>CONTRACT_NOT_FOUND</c>) is <c>409</c>. The MVC counterpart of
/// <see cref="LedgerProblemEndpointConventionBuilderExtensions.ProducesLedgerProblems{TBuilder}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ProducesLedgerProblemsAttribute : Attribute, IControllerModelConvention, IActionModelConvention
{
    /// <inheritdoc />
    public void Apply(ControllerModel controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        AddResponses(controller.Filters);
    }

    /// <inheritdoc />
    public void Apply(ActionModel action)
    {
        ArgumentNullException.ThrowIfNull(action);

        AddResponses(action.Filters);
    }

    private static void AddResponses(ICollection<Microsoft.AspNetCore.Mvc.Filters.IFilterMetadata> filters)
    {
        foreach (var (statusCode, responseType) in LedgerProblemResponses.All)
            filters.Add(new ProducesResponseTypeAttribute(responseType, statusCode, LedgerProblemResponses.ProblemJson));
    }
}
