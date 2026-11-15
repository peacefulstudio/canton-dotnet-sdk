// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;
using Xunit.Sdk;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// Asserts on an <see cref="ExerciseOutcome{T}"/> so a failure names the actual case and its payload,
/// where <c>Assert.IsType</c> prints the same nested type name for Expected and Actual and drops it.
/// </summary>
internal static class ExerciseOutcomeAssert
{
    /// <summary>Returns the <c>One</c> case, or fails the test with the actual case and its payload.</summary>
    public static ExerciseOutcome<T>.One IsOne<T>(ExerciseOutcome<T> outcome) =>
        outcome as ExerciseOutcome<T>.One
        ?? throw new XunitException($"Expected ExerciseOutcome.One but got {Describe(outcome)}");

    private static string Describe<T>(ExerciseOutcome<T> outcome) => outcome switch
    {
        ExerciseOutcome<T>.InfraError e =>
            $"InfraError (Status: {e.Status}, Category: {e.Category?.ToString() ?? "none"}, Message: {e.Message}){Cause(e.SourceException)}",
        ExerciseOutcome<T>.DamlError e =>
            $"DamlError (Category: {e.Category}, ErrorId: {e.ErrorId}, Message: {e.Message}, Metadata: [{string.Join(", ", e.Metadata.Select(pair => $"{pair.Key}={pair.Value}"))}])",
        ExerciseOutcome<T>.CommittedUndecodable e =>
            $"CommittedUndecodable (UpdateId: {e.UpdateId ?? "none"}, Message: {e.Message}){Cause(e.SourceException)}",
        ExerciseOutcome<T>.Many e =>
            $"Many (ContractIds: [{string.Join(", ", e.ContractIds)}])",
        ExerciseOutcome<T>.None => "None",
        null => "null",
        _ => outcome.ToString() ?? outcome.GetType().Name,
    };

    private static string Cause(Exception? exception) =>
        exception is null ? string.Empty : $"{Environment.NewLine}Source exception: {exception}";
}
