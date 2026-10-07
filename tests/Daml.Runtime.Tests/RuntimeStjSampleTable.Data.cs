// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static void AddData(Dictionary<Type, object[]> samples)
    {
        samples[typeof(LedgerOffset)] = [LedgerOffset.At(123), LedgerOffset.At(9_000_000_000)];
        samples[typeof(StakeholderResume)] = [new StakeholderResume(LedgerOffset.At(77))];
        samples[typeof(DamlJsonDeserializationLimits)] = [new DamlJsonDeserializationLimits(1000, 50)];
        samples[typeof(Party)] = [Alice, Bob];
        samples[typeof(SynchronizerId)] = [GlobalSynchronizer, PrivateSynchronizer];
        samples[typeof(Identifier)] = [SampleIdentifier, OtherIdentifier];
        samples[typeof(DamlField)] = [DamlField.Create("amount", new DamlInt64(42)), DamlField.Create("owner", new DamlParty(Alice.Value))];
        samples[typeof(DamlValue)] =
        [
            new DamlInt64(42),
            new DamlNumeric(1234.5678m),
            new DamlText("hello"),
            new DamlBool(true),
            DamlUnit.Instance,
            new DamlDate(new DateOnly(2026, 10, 5)),
            new DamlTimestamp(SampleInstant),
            new DamlParty(Alice.Value),
            DamlOptional.Some(new DamlInt64(7)),
            DamlOptional.None,
            DamlOptionalChain.Some(new DamlText("chained")),
            DamlOptionalChain.None,
            DamlList.Create(new DamlInt64(1), new DamlInt64(2)),
            DamlTextMap.Create(("first", new DamlInt64(1)), ("second", new DamlInt64(2))),
            DamlGenMap.Create((new DamlText("k1"), new DamlInt64(1)), (new DamlText("k2"), new DamlInt64(2))),
            DamlRecord.Create(SampleIdentifier, DamlField.Create("amount", new DamlInt64(42)), DamlField.Create("owner", new DamlParty(Alice.Value))),
            DamlVariant.Create(SampleIdentifier, "Left", new DamlText("payload")),
            DamlEnum.Create(SampleIdentifier, "Red"),
            new DamlContractId("00c0ffee", SampleIdentifier),
            SampleUndecodedJson,
        ];
    }
}
