// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

/// <summary>
/// Verifies the authorization routing of <see cref="ILedgerClient"/>: the
/// <see cref="SubmitterInfo"/> members are the primitives implementations override,
/// a lone <c>Party</c> argument converts to a submitter carrying that one act-as party
/// and no read-as parties, and multi-party / read-as submissions carry every party
/// through to the primitive instead of throwing.
/// </summary>
public class LedgerClientSubmitterInfoTests
{
    private static readonly ExerciseCommand SampleCommand = new(
        new Identifier("pkg", "Module", "Template"),
        ContractId: new ContractId<SampleTemplate>("cid-1"),
        Choice: new ChoiceName("DoIt"),
        ChoiceArgument: new DamlRecord(null, []));

    private static readonly SubmitterInfo MultiParty = new(
        actAs: new HashSet<Party> { new("alice"), new("bob") });

    private static readonly SubmitterInfo SinglePartyWithReadAs = new(
        actAs: new HashSet<Party> { new("alice") },
        readAs: new HashSet<Party> { new("observer") });

    [Fact]
    public async Task ExerciseAsync_with_result_Party_actAs_forwards_to_SubmitterInfo_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExerciseAsync_void_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateAsync_Party_argument_converts_to_a_submitter_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastCreateSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_Party_argument_converts_to_a_submitter_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeActiveAsync_Party_argument_converts_to_a_submitter_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeActiveSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExerciseAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task TryCreateAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastCreateSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task ExerciseAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
    }

    [Fact]
    public async Task SubscribeActiveAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeActiveAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeActiveAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeActiveSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeActiveSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task TryCreateAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateAsync(new FakeTemplate(), MultiParty, cancellationToken: TestContext.Current.CancellationToken);

        fake.LastCreateSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
        fake.LastCreateSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
        fake.LastSubscribeSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryExerciseAsync_Party_argument_converts_to_a_submitter_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastExerciseSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExerciseAsync_with_result_Party_actAs_forwards_commandId_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), commandId: new CommandId("caller-supplied"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseCommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task ExerciseAsync_with_result_SubmitterInfo_forwards_commandId_to_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, MultiParty, commandId: new CommandId("caller-supplied"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseCommandId.Should().Be(new CommandId("caller-supplied"));
    }

    [Fact]
    public async Task TryCreateOneByExerciseAsync_Party_actAs_forwards_timeout_to_the_transaction_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateOneByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(9), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionTimeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_Party_actAs_forwards_to_transaction_primitive_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateManyByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastTransactionSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task TryCreateManyByExerciseAsync_Party_actAs_forwards_timeout_to_the_transaction_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.TryCreateManyByExerciseAsync<FakeTemplate>(SampleCommand, new Party("alice"), timeout: TimeSpan.FromSeconds(9), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastTransactionTimeout.Should().Be(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public async Task ExerciseAsync_extension_reaches_the_primitive_with_null_timeout()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await client.ExerciseAsync<int>(SampleCommand, new Party("alice"), cancellationToken: TestContext.Current.CancellationToken);

        fake.LastExerciseSubmitter.Should().NotBeNull();
        fake.LastExerciseTimeout.Should().BeNull();
    }

    [Fact]
    public async Task SubscribeAsync_accepts_a_StakeholderResume_and_forwards_its_offset_to_the_SubmitterInfo_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;
        var resume = new StakeholderResume(LedgerOffset.At(10));

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(
            new SubmitterInfo(new Party("alice")), resume, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeFromOffset.Should().Be(resume.Offset);
    }

    [Fact]
    public async Task SubscribeAsync_Party_argument_converts_and_the_StakeholderResume_ticket_offset_reaches_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;
        var resume = new StakeholderResume(LedgerOffset.At(21));

        await foreach (var _ in client.SubscribeAsync<FakeTemplate>(new Party("alice"), resume, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeFromOffset.Should().Be(resume.Offset);
    }

    [Fact]
    public async Task SubscribeAsync_interface_family_accepts_a_StakeholderResume_and_forwards_its_offset_to_the_primitive()
    {
        var fake = new RecordingLedgerClient();
        ILedgerStreamer streamer = fake;
        var resume = new StakeholderResume(LedgerOffset.At(15));

        await foreach (var _ in streamer.SubscribeAsync(
            FakeViewDescriptor, new SubmitterInfo(new Party("alice")), resume, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeInterfaceFromOffset.Should().Be(resume.Offset);
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_Party_argument_converts_to_a_submitter_with_single_actAs_no_readAs()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(new Party("alice"), cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_with_single_actAs_and_readAs_carries_readAs_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(SinglePartyWithReadAs, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Value).Should().Equal("alice");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Select(p => p.Value).Should().Equal("observer");
    }

    [Fact]
    public async Task SubscribeLedgerEffectsAsync_with_multi_party_actAs_carries_all_parties_through_without_throwing()
    {
        var fake = new RecordingLedgerClient();
        ILedgerClient client = fake;

        await foreach (var _ in client.SubscribeLedgerEffectsAsync<FakeTemplate>(MultiParty, cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ActAs.Select(p => p.Value).Should().BeEquivalentTo("alice", "bob");
        fake.LastSubscribeLedgerEffectsSubmitter!.Value.ReadAs.Should().BeEmpty();
    }

    /// <summary>
    /// Records what each primitive receives, limited to the values something other than
    /// the caller decides: the <see cref="SubmitterInfo"/> a <c>Party</c> argument or a
    /// multi-party set resolves to, the <c>timeout</c> and <c>commandId</c> the throwing
    /// and create-by-exercise extensions compute, and the <c>fromOffset</c> a
    /// <see cref="StakeholderResume"/> ticket resolves to on the default interface member.
    /// </summary>
    private class RecordingLedgerClient : NotSupportedLedgerClient
    {
        private static readonly TransactionResult SampleTransaction = new(
            "update-id", LedgerOffset.Begin, [], [], new CommandId("cmd-id"));

        public SubmitterInfo? LastExerciseSubmitter { get; private set; }
        public SubmitterInfo? LastCreateSubmitter { get; private set; }
        public SubmitterInfo? LastTransactionSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeActiveSubmitter { get; private set; }
        public SubmitterInfo? LastSubscribeLedgerEffectsSubmitter { get; private set; }
        public TimeSpan? LastExerciseTimeout { get; private set; }
        public TimeSpan? LastTransactionTimeout { get; private set; }
        public CommandId? LastExerciseCommandId { get; private set; }
        public LedgerOffset? LastSubscribeFromOffset { get; private set; }
        public LedgerOffset? LastSubscribeInterfaceFromOffset { get; private set; }

        public override Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastExerciseSubmitter = submitter;
            LastExerciseTimeout = timeout;
            LastExerciseCommandId = commandId;
            return Task.FromResult<ExerciseOutcome<TResult>>(new ExerciseOutcome<TResult>.One(default(TResult)!));
        }

        public override Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var effective = submission.WithSubmitter(submitter);
            var actAs = effective.ActAs ?? [];
            var readAs = effective.ReadAs ?? [];
            LastTransactionSubmitter = new SubmitterInfo(
                new HashSet<Party>(actAs), readAs.Count > 0 ? new HashSet<Party>(readAs) : null);
            LastTransactionTimeout = timeout;
            return Task.FromResult<ExerciseOutcome<TransactionResult>>(
                new ExerciseOutcome<TransactionResult>.One(SampleTransaction));
        }

        public override Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            LastCreateSubmitter = submitter;
            return Task.FromResult<ExerciseOutcome<ContractId<TTemplate>>>(
                new ExerciseOutcome<ContractId<TTemplate>>.None());
        }

        public override IAsyncEnumerable<InterfaceStreamEvent<TInterface, TView>> SubscribeAsync<TInterface, TView>(
            ViewDescriptor<TInterface, TView> view,
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
        {
            LastSubscribeInterfaceFromOffset = fromOffset;
            return EmptyAsync<InterfaceStreamEvent<TInterface, TView>>(cancellationToken);
        }

        public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
        {
            LastSubscribeSubmitter = submitter;
            LastSubscribeFromOffset = fromOffset;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public override IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            CancellationToken cancellationToken = default)
        {
            LastSubscribeLedgerEffectsSubmitter = submitter;
            return EmptyAsync<ContractStreamEvent<T>>(cancellationToken);
        }

        public override IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset = null,
            CancellationToken cancellationToken = default)
        {
            LastSubscribeActiveSubmitter = submitter;
            return EmptyAsync<AcsSnapshotEntry<T>>(cancellationToken);
        }

        public override Task<LedgerOffset> GetLedgerEndAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(LedgerOffset.Begin);

        private static async IAsyncEnumerable<TItem> EmptyAsync<TItem>(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    /// <summary>
    /// Minimal <see cref="ITemplate"/> for routing tests. Carries no payload —
    /// the assertions here are about which submitter reaches the primitive,
    /// not about template encoding.
    /// </summary>
    private sealed record FakeTemplate : ITemplate, IDamlRecord<FakeTemplate>
    {
        public static Identifier TemplateId { get; } =
            new("pkg", "Module", "FakeTemplate");

        public static string PackageId => "pkg";
        public static string PackageName => "fake";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => new(TemplateId, []);

        public static FakeTemplate FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Minimal <see cref="IDamlInterface"/> marker for routing tests, paired with
    /// <see cref="FakeInterfaceView"/> through <see cref="FakeViewDescriptor"/>.
    /// </summary>
    private sealed record FakeInterfaceMarker : IDamlInterface, IHasView<FakeInterfaceView>
    {
        public static Identifier InterfaceId { get; } = new("pkg", "Module", "FakeInterface");
        public static string PackageId => "pkg";
        public static string PackageName => "fake";
        public static Version PackageVersion { get; } = new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record FakeInterfaceView : IDamlRecord<FakeInterfaceView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();

        public static FakeInterfaceView FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private static ViewDescriptor<FakeInterfaceMarker, FakeInterfaceView> FakeViewDescriptor { get; } = new();
}
