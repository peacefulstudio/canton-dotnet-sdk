// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;

namespace Canton.Ledger.Abstractions;

/// <summary>
/// Operation-context accessors for <see cref="LedgerOperationException"/>.
/// The upstream exception type (owned by <c>Daml.Ledger.Abstractions</c>) has no
/// <c>Operation</c> member yet, so the operation label attached by the
/// <see cref="ExerciseOutcomeExtensions"/> throw helpers travels in
/// <see cref="System.Exception.Data"/> and surfaces here as an extension property.
/// </summary>
public static class LedgerOperationExceptionExtensions
{
    internal const string OperationDataKey = OutcomeUnwrapPolicy.OperationDataKey;

    /// <summary>Extension members for <see cref="LedgerOperationException"/>.</summary>
    extension(LedgerOperationException exception)
    {
        /// <summary>
        /// The label of the ledger operation that failed, <c>{Template}.{Choice}</c> or
        /// <c>{Template}.Create</c> (for example <c>ConnectionRequest.Accept</c>), read off the
        /// <see cref="Daml.Runtime.Outcomes.ExerciseOutcome{T}.Operation"/> that
        /// <see cref="ExerciseOutcomeExtensions.OneOrThrow{T}(Daml.Runtime.Outcomes.ExerciseOutcome{T})"/>
        /// or a sibling helper unwrapped; <c>null</c> when the exception was raised without
        /// operation context (e.g. by the upstream throwing wrappers, or from an outcome that
        /// carries no operation).
        /// </summary>
        public string? Operation => exception.Data[OperationDataKey] as string;
    }
}
