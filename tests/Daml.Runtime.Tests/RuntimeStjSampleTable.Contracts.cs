// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static ContractKey SampleContractKey =>
        new(new DamlText("asset-1"), SampleIdentifier) { KeyHash = "hash-1" };

    private static EquatableArray<Party> Witnesses => Eq(Alice, Bob);

    private static CreatedEvent SampleCreatedEvent =>
        new("1:0", "00c0ffee", SampleIdentifier, SampleRecord, Witnesses, Eq(Alice), Eq(Bob, Carol), SampleContractKey, SampleInstant);

    private static ExercisedEvent SampleExercisedEvent =>
        new("00c0ffee", SampleIdentifier, OtherIdentifier, new ChoiceName("Transfer"), new DamlParty(Bob.Value), new DamlInt64(5), true, Eq(Alice, Carol), Witnesses);

    private static CreatedContract SampleCreatedContract =>
        new("1:0", "00c0ffee", SampleIdentifier, SampleRecord, Witnesses, Eq(Alice), Eq(Bob, Carol), SampleContractKey, SampleInstant)
        {
            InterfaceIds = Eq(SampleIdentifier, OtherIdentifier),
            UndecodedPayload = SampleUndecodedJson,
        };

    private static TreeEvent.Created SampleTreeCreated =>
        new("1:1", "00c0ffee", SampleIdentifier, SampleRecord, Witnesses, Eq(Alice), Eq(Bob, Carol), SampleContractKey, SampleInstant)
        {
            InterfaceIds = Eq(SampleIdentifier, OtherIdentifier),
            UndecodedCreateArguments = SampleUndecodedJson,
        };

    private static TreeEvent.Exercised SampleTreeExercised =>
        new(
            "1:0",
            "00c0ffee",
            SampleIdentifier,
            OtherIdentifier,
            new ChoiceName("Transfer"),
            new DamlParty(Bob.Value),
            new DamlInt64(5),
            true,
            Eq(Alice, Carol),
            Witnesses,
            Eq<TreeEvent>(SampleTreeCreated, SampleTreeCreated with { EventId = "1:2" }));

    private static void AddContracts(Dictionary<Type, object[]> samples)
    {
        samples[typeof(DamlTypeKind)] = [DamlTypeKind.Interface];
        samples[typeof(TemplateIdFormat)] = [TemplateIdFormat.PackageHash];
        samples[typeof(EquatableArray<string>)] = [Eq("first", "second")];
        samples[typeof(EquatableArray<Party>)] = [Witnesses];
        samples[typeof(ContractId<SampleTemplate>)] = [new ContractId<SampleTemplate>("00c0ffee")];
        samples[typeof(ContractId<ISampleInterface>)] = [new ContractId<ISampleInterface>("00facade")];
        samples[typeof(ContractKey)] = [SampleContractKey];
        samples[typeof(ContractKey<string>)] = [new ContractKey<string>("asset-1", "hash-1")];
        samples[typeof(Contract<SampleTemplate>)] =
        [
            new Contract<SampleTemplate>(new ContractId<SampleTemplate>("00c0ffee"), new SampleTemplate(Alice, 42)),
        ];
        samples[typeof(Contract<KeyedSampleTemplate, string>)] =
        [
            new Contract<KeyedSampleTemplate, string>(
                new ContractId<KeyedSampleTemplate>("00beef"),
                new KeyedSampleTemplate(Alice, "asset-1"),
                new ContractKey<string>("asset-1", "hash-1")),
        ];
        samples[typeof(ActiveContract<SampleTemplate>)] =
        [
            new ActiveContract<SampleTemplate>(
                new SampleTemplate(Alice, 42),
                LedgerOffset.At(123),
                GlobalSynchronizer)
            {
                Disclosure = SampleDisclosedContract,
            },
        ];
        samples[typeof(CreatedEvent)] = [SampleCreatedEvent];
        samples[typeof(ArchivedEvent)] = [new ArchivedEvent("1:3", "00c0ffee", SampleIdentifier, Witnesses)];
        samples[typeof(ExercisedEvent)] = [SampleExercisedEvent];
        samples[typeof(CreatedContract)] = [SampleCreatedContract];
        samples[typeof(SubmitAndWaitResult)] = [new SubmitAndWaitResult(new CommandId("cmd-1"), "update-1", LedgerOffset.At(321))];
        samples[typeof(TransactionResult)] =
        [
            new TransactionResult(
                "update-1",
                LedgerOffset.At(321),
                Eq(SampleCreatedContract, SampleCreatedContract with { EventId = "1:4" }),
                Eq("00dead", "00beef"),
                new CommandId("cmd-1"))
            {
                ExercisedEvents = Eq(SampleExercisedEvent, SampleExercisedEvent with { Consuming = false }),
            },
            new TransactionResult(
                "update-2",
                LedgerOffset.At(322),
                EquatableArray<CreatedContract>.Empty,
                Eq("00dead"),
                new CommandId("cmd-2")),
        ];
        samples[typeof(TransactionTree)] =
        [
            new TransactionTree(
                "update-1",
                LedgerOffset.At(321),
                Eq<TreeEvent>(SampleTreeExercised, SampleTreeCreated)),
        ];
        samples[typeof(TreeEvent)] = [SampleTreeCreated, SampleTreeExercised];
    }
}
