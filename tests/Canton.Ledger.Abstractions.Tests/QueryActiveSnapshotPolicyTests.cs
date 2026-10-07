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
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Daml.Testing.SnapshotPolicy;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Abstractions.Tests;

public sealed class QueryActiveSnapshotPolicyTests
{
    public static TheoryData<string> PolicyCase()
    {
        var data = new TheoryData<string>();
        foreach (var policyCase in SnapshotPolicyCases.Names)
        {
            data.Add(policyCase);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyCase))]
    public Task SnapshotPolicy_case_holds_on_QueryActiveAsync(string policyCase) =>
        SnapshotPolicyCases.RunAsync(policyCase, new QueryActiveDriver());

    private sealed class QueryActiveDriver : ISnapshotPolicyDriver
    {
        private static readonly Party Alice = new("alice");

        public SnapshotPolicySubject Subject { get; } =
            new("IPolicyHolding", "interface view", "SubscribeActiveAsync(IPolicyHolding.View, ...)");

        public async Task<IReadOnlyList<string>> DrainAsync(
            IReadOnlyList<SnapshotStep> script,
            bool streamHonoursCancellation,
            CancellationToken cancellationToken)
        {
            var entries = script.Select(ToEntry).ToArray();
            var client = Substitute.For<ICantonLedgerClient>();
            client
                .SubscribeActiveAsync(
                    Arg.Any<ViewDescriptor<IPolicyHolding, PolicyHoldingView>>(),
                    Arg.Any<SubmitterInfo>(),
                    Arg.Any<LedgerOffset?>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => Replay(entries, streamHonoursCancellation, call.Arg<CancellationToken>()));

            var contracts = await DefaultQueryActive(client, new SubmitterInfo(Alice), null, false, cancellationToken);

            return contracts.Select(contract => contract.Contract.Id.Value).ToList();
        }

        private delegate Task<IReadOnlyList<ActiveContract<InterfaceContract<IPolicyHolding, PolicyHoldingView>>>> QueryActive(
            ICantonLedgerClient client,
            SubmitterInfo submitter,
            LedgerOffset? activeAtOffset,
            bool includeDisclosure,
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
                [typeof(ICantonLedgerClient), typeof(SubmitterInfo), typeof(LedgerOffset?), typeof(bool), typeof(CancellationToken)],
                typeof(QueryActiveDriver).Module,
                skipVisibility: true);
            var il = invokeNonVirtually.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg, (short)4);
            il.Emit(OpCodes.Call, defaultBody);
            il.Emit(OpCodes.Ret);

            return (QueryActive)invokeNonVirtually.CreateDelegate(typeof(QueryActive));
        }

        private static async IAsyncEnumerable<InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>> Replay(
            InterfaceAcsSnapshotEntry<IPolicyHolding, PolicyHoldingView>[] entries,
            bool streamHonoursCancellation,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var entry in entries)
            {
                if (streamHonoursCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                yield return entry;
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
