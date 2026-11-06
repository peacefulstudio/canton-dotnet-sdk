// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Daml.Testing.SnapshotPolicy;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public sealed class QueryActiveSnapshotPolicyTests
{
    public static TheoryData<string, UnreadableRows> PolicyCase()
    {
        var data = new TheoryData<string, UnreadableRows>();
        foreach (var (policyCase, policy) in SnapshotPolicyCases.Combinations)
        {
            data.Add(policyCase, policy);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyCase))]
    public Task SnapshotPolicy_case_holds_on_QueryActiveAsync(string policyCase, UnreadableRows policy) =>
        SnapshotPolicyCases.RunAsync(policyCase, new QueryActiveDriver(policy));

    private sealed class QueryActiveDriver(UnreadableRows policy) : ISnapshotPolicyDriver
    {
        private static readonly Party Alice = new("alice");

        public UnreadableRows Policy { get; } = policy;

        public SnapshotPolicySubject Subject { get; } =
            new("IPolicyHolding", "interface view", "SubscribeActiveAsync(IPolicyHolding.View, ...)");

        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WhenStalled => _stalled.Task;

        public async Task<SnapshotRead> DrainAsync(
            IReadOnlyList<SnapshotStep> script,
            bool streamHonoursCancellation,
            TimeSpan? timeout,
            TimeProvider timeProvider,
            CancellationToken cancellationToken)
        {
            var client = Substitute.For<ICantonLedgerClient>();
            client.TimeProvider.Returns(timeProvider);
            client
                .SubscribeActiveAsync(
                    Arg.Any<ViewDescriptor<IPolicyHolding, PolicyHoldingView>>(),
                    Arg.Any<SubmitterInfo>(),
                    Arg.Any<LedgerOffset?>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => Replay(script, streamHonoursCancellation, call.Arg<CancellationToken>()));

            var contracts = await DefaultQueryActive(
                client, new SubmitterInfo(Alice), null, false, timeout, Policy, cancellationToken);

            return new SnapshotRead(contracts.Select(contract => contract.Contract.Id.Value).ToList(), contracts.Unreadable);
        }

        private delegate Task<ActiveSnapshot<ActiveContract<InterfaceContract<IPolicyHolding, PolicyHoldingView>>>> QueryActive(
            ICantonLedgerClient client,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset,
            bool includeDisclosure,
            TimeSpan? timeout,
            UnreadableRows unreadableRows,
            CancellationToken cancellationToken);

        private static readonly QueryActive DefaultQueryActive = BindDefaultQueryActive();

        // Workaround: NSubstitute (Castle DynamicProxy) intercepts default interface methods and returns
        // an empty result, so the QueryActiveAsync default body is invoked non-virtually over the substitute.
        private static QueryActive BindDefaultQueryActive()
        {
            var defaultBody = typeof(ICantonLedgerClient)
                .GetMethod(nameof(ICantonLedgerClient.QueryActiveAsync))!
                .MakeGenericMethod(typeof(IPolicyHolding), typeof(PolicyHoldingView));

            var invokeNonVirtually = new DynamicMethod(
                "QueryActiveAsyncDefaultBody",
                defaultBody.ReturnType,
                [typeof(ICantonLedgerClient), typeof(SubmitterInfo), typeof(LedgerOffset?), typeof(bool), typeof(TimeSpan?), typeof(UnreadableRows), typeof(CancellationToken)],
                typeof(QueryActiveDriver).Module,
                skipVisibility: true);
            var il = invokeNonVirtually.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg, (short)4);
            il.Emit(OpCodes.Ldarg, (short)5);
            il.Emit(OpCodes.Ldarg, (short)6);
            il.Emit(OpCodes.Call, defaultBody);
            il.Emit(OpCodes.Ret);

            return (QueryActive)invokeNonVirtually.CreateDelegate(typeof(QueryActive));
        }

        private async IAsyncEnumerable<InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>> Replay(
            IReadOnlyList<SnapshotStep> script,
            bool streamHonoursCancellation,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var step in script)
            {
                if (streamHonoursCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (step is SnapshotStep.Stall stall)
                {
                    _stalled.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(
                        static _ => { },
                        CancellationToken.None);

                    if (stall.Ending == StallEnding.ThrowsCancellation)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (stall.Ending == StallEnding.ReportsCancelledFaultInBand)
                    {
                        yield return new InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>.StreamError(
                            new TransportStatus.Grpc(GrpcStatusCode.Cancelled), "CANCELLED", null, null, null);
                    }

                    yield break;
                }

                yield return ToEntry(step);
                await Task.Yield();
            }
        }

        private static InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView> ToEntry(SnapshotStep step) =>
            step switch
            {
                SnapshotStep.Row row => new InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>.Created(
                    new ContractId<IPolicyHolding>(row.ContractId),
                    new PolicyHoldingView(1m),
                    null,
                    LedgerOffset.At(row.Offset),
                    new SynchronizerId("sync"),
                    [Alice]),
                SnapshotStep.Checkpoint checkpoint =>
                    new InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>.Checkpoint(
                        new StakeholderResume(LedgerOffset.At(checkpoint.Offset))),
                SnapshotStep.Fault fault =>
                    new InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>.StreamError(
                        fault.Status, fault.Message, fault.Category, fault.ErrorId, fault.SourceException),
                SnapshotStep.Unclassified unclassified =>
                    new InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>.Unclassified(
                        unclassified.Offset is { } offset ? LedgerOffset.At(offset) : null,
                        unclassified.Kind,
                        unclassified.RawKind),
                _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
            };
    }

    public interface IPolicyHolding : IDamlInterface, IHasView<PolicyHoldingView>
    {
        static Identifier IDamlInterface.InterfaceId => InterfaceId;
        public static new Identifier InterfaceId { get; } = new("pkg", "Module", "IPolicyHolding");
        static string IDamlInterface.PackageId => "pkg";
        static string IDamlInterface.PackageName => "pkg-name";
        static Version IDamlInterface.PackageVersion => new(0, 1, 0);

        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("pkg", "Module", "IPolicyHolding"), DamlTypeKind.Interface, "pkg-name");
    }

    public sealed record PolicyHoldingView(decimal Amount) : IDamlRecord, IDamlRecord<PolicyHoldingView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("amount", new DamlNumeric(Amount)));

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();

        public static PolicyHoldingView FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }
}
