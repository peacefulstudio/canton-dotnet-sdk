// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private const string DamlValueDeclaredAbstract =
        "DamlValue is abstract and no converter dispatches on its arm, so a value declared as DamlValue is written as an empty object and cannot be read back";

    private const string DamlValueMember =
        "the sample holds a DamlValue member (a field value, key, argument or element), DamlValue is abstract and no converter dispatches on its arm, so reading it throws NotSupportedException";

    private const string GenMapTupleEntries =
        "DamlGenMap.Entries is a list of ValueTuple<DamlValue, DamlValue>, STJ skips the tuple's Item1 and Item2 fields, so every entry reads back as a default pair and the constructor rejects the repeated empty key; its DamlValue elements also have no converter";

    private const string TreeEventAbstract =
        "TreeEvent is abstract and, unlike the other event unions, has no discriminated union converter, so a value declared as TreeEvent cannot be read back";

    private const string TransactionTreeCause =
        TreeEventAbstract + ", and the events hold DamlValue members, which have no converter either";

    private static Dictionary<string, string> BuildKnownBroken()
    {
        var knownBroken = new Dictionary<string, string>();
        Broken(knownBroken, DamlValueDeclaredAbstract,
            "DamlValue/DamlBool",
            "DamlValue/DamlContractId",
            "DamlValue/DamlDate",
            "DamlValue/DamlEnum",
            "DamlValue/DamlGenMap",
            "DamlValue/DamlInt64",
            "DamlValue/DamlList",
            "DamlValue/DamlNumeric",
            "DamlValue/DamlOptional~1",
            "DamlValue/DamlOptional~2",
            "DamlValue/DamlOptionalChain~1",
            "DamlValue/DamlOptionalChain~2",
            "DamlValue/DamlParty",
            "DamlValue/DamlRecord",
            "DamlValue/DamlText",
            "DamlValue/DamlTextMap",
            "DamlValue/DamlTimestamp",
            "DamlValue/DamlUndecodedJson",
            "DamlValue/DamlUnit",
            "DamlValue/DamlVariant");
        Broken(knownBroken, DamlValueMember,
            "AcsSnapshotEntry<SampleTemplate>/Created",
            "AcsSnapshotEntry<SampleTemplate>/Created/as-self",
            "ContractKey",
            "ContractStreamEvent<SampleTemplate>/Assigned",
            "ContractStreamEvent<SampleTemplate>/Assigned/as-self",
            "ContractStreamEvent<SampleTemplate>/Created",
            "ContractStreamEvent<SampleTemplate>/Created/as-self",
            "ContractStreamEvent<SampleTemplate>/Exercised",
            "ContractStreamEvent<SampleTemplate>/Exercised/as-self",
            "CreatedContract",
            "CreatedEvent",
            "DamlField~1",
            "DamlField~2",
            "DamlValue/DamlList/as-self",
            "DamlValue/DamlOptional~1/as-self",
            "DamlValue/DamlOptionalChain~1/as-self",
            "DamlValue/DamlRecord/as-self",
            "DamlValue/DamlTextMap/as-self",
            "DamlValue/DamlVariant/as-self",
            "ExercisedEvent",
            "InterfaceAcsSnapshotEntry<ISampleInterface,SampleView>/Created",
            "InterfaceAcsSnapshotEntry<ISampleInterface,SampleView>/Created/as-self",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Assigned",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Assigned/as-self",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Created",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Created/as-self",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Exercised",
            "InterfaceStreamEvent<ISampleInterface,SampleView>/Exercised/as-self",
            "TransactionResult~1",
            "TreeEvent/Created/as-self",
            "TreeEvent/Exercised/as-self");
        Broken(knownBroken, GenMapTupleEntries,
            "DamlValue/DamlGenMap/as-self");
        Broken(knownBroken, TreeEventAbstract,
            "TreeEvent/Created",
            "TreeEvent/Exercised");
        Broken(knownBroken, TransactionTreeCause, "TransactionTree");
        return knownBroken;
    }

    private static void Broken(Dictionary<string, string> knownBroken, string rootCause, params string[] caseIds)
    {
        foreach (var caseId in caseIds)
        {
            knownBroken.Add(caseId, rootCause);
        }
    }
}
