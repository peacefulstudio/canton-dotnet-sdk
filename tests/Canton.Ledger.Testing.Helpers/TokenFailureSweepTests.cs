// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// One sweep that every transport's real client is run against with a token provider that always
/// fails. Acquiring the token is the first step of every call, so a failure there means nothing was
/// sent: a throwing call must raise a <see cref="LedgerOperationException"/> with
/// <see cref="TransportStatus.NoResponse"/>, <see cref="CommitState.NotCommitted"/> and a
/// <see cref="TokenAcquisitionException"/> as its inner exception; a <c>Try*</c> call must return an
/// <see cref="ExerciseOutcome{T}.InfraError"/> carrying that exception; a stream must end in a single
/// terminal error carrying it. The sweep asserts the transport received no request, and a completeness
/// gate reflects over <see cref="ICantonLedgerClient"/> and <see cref="IAdminClient"/> so a new entry
/// point cannot ship without token-failure coverage on both transports.
/// </summary>
public abstract class TokenFailureSweepTests
{
    private const string TokenFailureMessage = "the identity provider is unreachable";

    private static readonly Party Alice = new("alice::ns1");
    private static readonly RuntimeCommands.SubmitterInfo Submitter = new(Alice);
    private static readonly ViewDescriptor<InterfaceMarker, InterfaceMarkerView> View = new();

    private static readonly Dictionary<string, Func<ICantonLedgerClient, CancellationToken, Task<Failure>>> TryMembers = new()
    {
        ["TryExerciseAsync(6)"] = async (ledger, token) => Failure.OfOutcome(await ledger.TryExerciseAsync<DamlUnit>(
            new RuntimeCommands.ExerciseCommand(
                TemplateMarker.TemplateId,
                new ContractId<TemplateMarker>("00abc"),
                new RuntimeCommands.ChoiceName("Transfer"),
                DamlUnit.Instance),
            Submitter,
            cancellationToken: token)),
        ["TryCreateAsync(6)"] = async (ledger, token) => Failure.OfOutcome(await ledger.TryCreateAsync(
            new TemplateMarker(Alice), Submitter, cancellationToken: token)),
        ["TrySubmitAndWaitForTransactionAsync(4)"] = async (ledger, token) => Failure.OfOutcome(
            await ledger.TrySubmitAndWaitForTransactionAsync(EntryPointInvocations.Submission(), Submitter, cancellationToken: token)),
        ["TrySubmitAndWaitForTransactionTreeAsync(4)"] = async (ledger, token) => Failure.OfOutcome(
            await ledger.TrySubmitAndWaitForTransactionTreeAsync(EntryPointInvocations.Submission(), Submitter, cancellationToken: token)),
        ["TrySubmitAndWaitForReassignmentAsync(3)"] = async (ledger, token) => Failure.OfOutcome(
            await ledger.TrySubmitAndWaitForReassignmentAsync<TemplateMarker>(
                ReassignmentSubmission.Of(new UnassignCommand("00abc", new SynchronizerId("sync::a"), new SynchronizerId("sync::b")), Alice),
                cancellationToken: token)),
    };

    private static readonly Dictionary<string, Func<ICantonLedgerClient, CancellationToken, Task<StreamFailure>>> StreamMembers = new()
    {
        ["CompletionStreamAsync(3)"] = (ledger, token) => StreamFailure.Of(
            ledger.CompletionStreamAsync(Submitter, LedgerOffset.At(5), token),
            streamEvent => streamEvent is CompletionStreamEvent.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["GetCompletionsAsync(3)"] = (ledger, token) => StreamFailure.Of(
            ledger.GetCompletionsAsync([Alice], LedgerOffset.At(5), token),
            streamEvent => streamEvent is CompletionStreamEvent.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeAsync(4)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeAsync<TemplateMarker>(Submitter, LedgerOffset.At(5), LedgerOffset.At(9), token),
            streamEvent => streamEvent is ContractStreamEvent<TemplateMarker>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeActiveAsync(4)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeActiveAsync<TemplateMarker>(Submitter, LedgerOffset.At(5), cancellationToken: token),
            streamEvent => streamEvent is AcsSnapshotEntry<TemplateMarker>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeLedgerEffectsAsync(4)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeLedgerEffectsAsync<TemplateMarker>(Submitter, LedgerOffset.At(5), LedgerOffset.At(9), token),
            streamEvent => streamEvent is ContractStreamEvent<TemplateMarker>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeAsync(5)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeAsync(View, Submitter, LedgerOffset.At(5), LedgerOffset.At(9), token),
            streamEvent => streamEvent is InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeActiveAsync(5)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeActiveAsync(View, Submitter, LedgerOffset.At(5), cancellationToken: token),
            streamEvent => streamEvent is InterfaceAcsSnapshotEntry<InterfaceMarker, InterfaceMarkerView>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
        ["SubscribeLedgerEffectsAsync(5)"] = (ledger, token) => StreamFailure.Of(
            ledger.SubscribeLedgerEffectsAsync(View, Submitter, LedgerOffset.At(5), LedgerOffset.At(9), token),
            streamEvent => streamEvent is InterfaceStreamEvent<InterfaceMarker, InterfaceMarkerView>.StreamError streamError ? new(streamError.Status, streamError.SourceException) : null),
    };

    /// <summary>
    /// Builds this transport's real ledger and admin clients over a stubbed participant, authenticating
    /// every call with <paramref name="tokenProvider"/>.
    /// </summary>
    protected abstract (ICantonLedgerClient Ledger, IAdminClient Admin) CreateClients(ITokenProvider tokenProvider);

    /// <summary>A serialized prepared transaction in the encoding this transport requires.</summary>
    protected abstract byte[] PreparedTransaction { get; }

    /// <summary>How many requests the stubbed participant received since <see cref="CreateClients"/>.</summary>
    protected abstract int RequestsReceived { get; }

    /// <summary>
    /// The reason this transport raises <see cref="NotSupportedException"/> for <paramref name="entryPoint"/>
    /// before it authenticates anything, or <c>null</c> when the member authenticates its request.
    /// </summary>
    protected abstract string? ServedWithoutAuthentication(string entryPoint);

    public static TheoryData<string> UnaryEntryPoints() => Names(MalformedResponseSweepTests.CommitStateByEntryPoint.Keys);

    public static TheoryData<string> TryEntryPoints() => Names(TryMembers.Keys);

    public static TheoryData<string> StreamEntryPoints() => Names(StreamMembers.Keys);

    [Theory]
    [MemberData(nameof(UnaryEntryPoints))]
    public async Task Unary_entry_point_reports_a_token_failure_as_a_never_sent_NoResponse(string entryPoint)
    {
        var (ledger, admin) = CreateClients(new FailingTokenProvider());
        var call = () => EntryPointInvocations.Invoke<TemplateMarker>(
            ledger, admin, entryPoint, PreparedTransaction, TestContext.Current.CancellationToken);

        if (ServedWithoutAuthentication(entryPoint) is not null)
        {
            await call.Should().ThrowAsync<NotSupportedException>();
        }
        else
        {
            var thrown = (await call.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
            thrown.Status.Should().Be(new TransportStatus.NoResponse());
            thrown.CommitState.Should().Be(CommitState.NotCommitted);
            AssertCarriesTheTokenFailure(thrown.InnerException);
        }

        RequestsReceived.Should().Be(0);
    }

    [Fact]
    public async Task QueryActiveAsync_reports_a_token_failure_as_a_never_sent_NoResponse()
    {
        var (ledger, _) = CreateClients(new FailingTokenProvider());

        var thrown = (await ledger
            .Invoking(client => client.QueryActiveAsync<InterfaceMarker, InterfaceMarkerView>(
                Submitter, LedgerOffset.At(5), cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<LedgerOperationException>()).Which;

        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        AssertCarriesTheTokenFailure(thrown.InnerException);
        RequestsReceived.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(TryEntryPoints))]
    public async Task Try_entry_point_reports_a_token_failure_as_an_InfraError_carrying_it(string entryPoint)
    {
        var (ledger, _) = CreateClients(new FailingTokenProvider());

        var failure = await TryMembers[entryPoint](ledger, TestContext.Current.CancellationToken);

        failure.Status.Should().Be(new TransportStatus.NoResponse());
        AssertCarriesTheTokenFailure(failure.Source);
        RequestsReceived.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(StreamEntryPoints))]
    public async Task Stream_entry_point_ends_in_one_terminal_error_carrying_a_token_failure(string entryPoint)
    {
        var (ledger, _) = CreateClients(new FailingTokenProvider());

        var failure = await StreamMembers[entryPoint](ledger, TestContext.Current.CancellationToken);

        failure.EventCount.Should().Be(1);
        failure.Error!.Status.Should().Be(new TransportStatus.NoResponse());
        AssertCarriesTheTokenFailure(failure.Error.Source);
        RequestsReceived.Should().Be(0);
    }

    [Fact]
    public void Every_non_unary_entry_point_of_the_client_interfaces_has_a_token_failure_row()
    {
        var abstractMembers = new[] { typeof(ICantonLedgerClient), typeof(IAdminClient) }
            .SelectMany(contract => new[] { contract }.Concat(contract.GetInterfaces()))
            .Distinct()
            .SelectMany(contract => contract.GetMethods())
            .Where(method => method.IsAbstract)
            .ToList();

        var tryKeys = abstractMembers
            .Where(method => method.Name.StartsWith("Try", StringComparison.Ordinal))
            .Select(Key)
            .ToHashSet();
        var streamKeys = abstractMembers
            .Where(method => method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
            .Select(Key)
            .ToHashSet();

        tryKeys.Should().BeEquivalentTo(TryMembers.Keys);
        streamKeys.Should().BeEquivalentTo(StreamMembers.Keys);
    }

    private static string Key(System.Reflection.MethodInfo method) => $"{method.Name}({method.GetParameters().Length})";

    private static TheoryData<string> Names(IEnumerable<string> names) => [.. names];

    private static void AssertCarriesTheTokenFailure(Exception? reported)
    {
        var acquisition = reported.Should().BeOfType<TokenAcquisitionException>().Subject;
        acquisition.InnerException.Should().BeOfType<HttpRequestException>().Which.Message.Should().Be(TokenFailureMessage);
    }

    private sealed class FailingTokenProvider : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new HttpRequestException(TokenFailureMessage));
    }

    private sealed record Failure(TransportStatus Status, Exception? Source)
    {
        internal static Failure OfOutcome<T>(ExerciseOutcome<T> outcome)
        {
            var infra = outcome.Should().BeOfType<ExerciseOutcome<T>.InfraError>().Subject;
            return new Failure(infra.Status, infra.SourceException);
        }
    }

    private sealed record StreamFailure(int EventCount, Failure? Error)
    {
        internal static async Task<StreamFailure> Of<TEvent>(IAsyncEnumerable<TEvent> stream, Func<TEvent, Failure?> asError)
        {
            var events = new List<TEvent>();
            await foreach (var streamEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
            {
                events.Add(streamEvent);
            }

            return new StreamFailure(events.Count, events.Select(asError).SingleOrDefault(streamEvent => streamEvent is not null));
        }
    }
}
