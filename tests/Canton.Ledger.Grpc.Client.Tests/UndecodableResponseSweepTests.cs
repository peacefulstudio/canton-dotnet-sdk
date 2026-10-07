// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class UndecodableResponseSweepTests
{
    private static readonly Party Alice = new("alice::ns1");
    private static readonly Daml.Runtime.Commands.SubmitterInfo Submitter = new(Alice);

    private static readonly string[] DeliberateFailureFragments =
    [
        "point reads only project transaction-shaped updates",
        "which cannot be read as",
        "its interface view is unavailable",
        "was served without field labels and is not visible to the event query",
        "pagination is not progressing",
        "pagination did not complete after",
    ];

    private static readonly Member[] Members =
    [
        .. MalformedResponseSweepTests.CommitStateByEntryPoint.Select(entry => new Member(
            entry.Key, entry.Value, clients => GrpcClientHarness.Invoke(clients, entry.Key))),
        new("GetContractAsync(4)/interface-kind", CommitState.NotCommitted, c => c.Ledger.GetContractAsync(new ContractId<MismatchedKindMarker>("00abc"), Submitter, cancellationToken: Token)),
        new("GetEventsByContractIdAsync(4)/interface-kind", CommitState.NotCommitted, c => c.Ledger.GetEventsByContractIdAsync(new ContractId<MismatchedKindMarker>("00abc"), Submitter, cancellationToken: Token)),
    ];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string> MemberNames()
    {
        var names = new TheoryData<string>();
        foreach (var member in Members)
        {
            names.Add(member.Name);
        }

        return names;
    }

    [Theory]
    [MemberData(nameof(MemberNames))]
    public async Task Member_never_lets_a_raw_exception_escape_a_response_it_received(string memberName)
    {
        var member = Members.Single(candidate => candidate.Name == memberName);
        var firstVariantByFailure = new Dictionary<string, (string Variant, int Count)>();
        var outcomes = new Dictionary<OutcomeKind, int>();

        foreach (var variant in WireResponseVariant.All)
        {
            var outcome = await Drive(member, variant);
            outcomes[outcome.Kind] = outcomes.GetValueOrDefault(outcome.Kind) + 1;
            if (outcome.Kind == OutcomeKind.Escaped)
            {
                var seen = firstVariantByFailure.GetValueOrDefault(outcome.Detail, (variant.Name, 0));
                firstVariantByFailure[outcome.Detail] = (seen.Item1, seen.Item2 + 1);
            }
        }

        var variantsThatReachedTheDecoder = member.Name.EndsWith("/interface-kind", StringComparison.Ordinal)
            ? outcomes.GetValueOrDefault(OutcomeKind.Undecodable)
            : outcomes.GetValueOrDefault(OutcomeKind.Succeeded);
        variantsThatReachedTheDecoder.Should().BePositive("the sweep must drive {0} past request validation into its response decoding", memberName);

        firstVariantByFailure
            .Select(entry => $"{entry.Key} (first in {entry.Value.Item1}, {entry.Value.Item2} variants)")
            .Should().BeEmpty();
    }

    private static async Task<Outcome> Drive(Member member, WireResponseVariant variant)
    {
        try
        {
            await member.Invoke(GrpcClientHarness.CreateClients(new CannedResponseCallInvoker(variant)));
            return new Outcome(OutcomeKind.Succeeded, string.Empty);
        }
        catch (LedgerOperationException undecodable)
        {
            return DescribeUndecodableMismatch(undecodable, member.CommitStateOfUndecodableResponse) is { } mismatch
                ? new Outcome(OutcomeKind.Escaped, mismatch)
                : new Outcome(OutcomeKind.Undecodable, string.Empty);
        }
        catch (Exception raw) when (IsDeliberateFailure(raw))
        {
            return new Outcome(OutcomeKind.Deliberate, string.Empty);
        }
        catch (Exception raw)
        {
            return new Outcome(OutcomeKind.Escaped, $"{raw.GetType().Name}: {raw.Message}");
        }
    }

    private static string? DescribeUndecodableMismatch(LedgerOperationException thrown, CommitState expectedCommitState)
    {
        if (thrown.Status is not TransportStatus.UndecodableBody)
        {
            return $"LedgerOperationException with status {thrown.Status}: {thrown.Message}";
        }

        if (thrown.InnerException is not MalformedResponseException)
        {
            return $"UndecodableBody whose inner exception is {thrown.InnerException?.GetType().Name ?? "null"}";
        }

        return thrown.CommitState == expectedCommitState
            ? null
            : $"UndecodableBody with commit state {thrown.CommitState}, expected {expectedCommitState}";
    }

    private static bool IsDeliberateFailure(Exception exception) =>
        exception is InvalidOperationException
        && DeliberateFailureFragments.Any(fragment => exception.Message.Contains(fragment, StringComparison.Ordinal));

    private enum OutcomeKind
    {
        Succeeded,
        Undecodable,
        Deliberate,
        Escaped,
    }

    private sealed record Outcome(OutcomeKind Kind, string Detail);

    private sealed record Member(string Name, CommitState CommitStateOfUndecodableResponse, Func<GrpcClients, Task> Invoke);
}
