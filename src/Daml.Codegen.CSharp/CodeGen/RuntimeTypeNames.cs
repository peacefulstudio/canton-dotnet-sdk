// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

internal static class RuntimeTypeNames
{
    public const string Party = nameof(Daml.Runtime.Data.Party);
    public const string DamlRecord = nameof(Daml.Runtime.Data.DamlRecord);
    public const string DamlField = nameof(Daml.Runtime.Data.DamlField);
    public const string DamlFieldAttribute = nameof(Daml.Runtime.Data.DamlFieldAttribute);
    public const string DamlFieldCollections = nameof(Daml.Runtime.Data.DamlFieldCollections);
    public const string DamlValue = nameof(Daml.Runtime.Data.DamlValue);
    public const string IDamlValue = nameof(Daml.Runtime.Data.IDamlValue);
    public const string IDamlRecord = nameof(Daml.Runtime.Data.IDamlRecord);
    public const string IDamlVariant = nameof(Daml.Runtime.Data.IDamlVariant);
    public const string DamlVariant = nameof(Daml.Runtime.Data.DamlVariant);
    public const string DamlEnum = nameof(Daml.Runtime.Data.DamlEnum);
    public const string DamlOptional = nameof(Daml.Runtime.Data.DamlOptional);
    public const string DamlOptionalChain = nameof(Daml.Runtime.Data.DamlOptionalChain);
    public const string DamlList = nameof(Daml.Runtime.Data.DamlList);
    public const string DamlTextMap = nameof(Daml.Runtime.Data.DamlTextMap);
    public const string DamlGenMap = nameof(Daml.Runtime.Data.DamlGenMap);
    public const string DamlInt64 = nameof(Daml.Runtime.Data.DamlInt64);
    public const string DamlNumeric = nameof(Daml.Runtime.Data.DamlNumeric);
    public const string DamlText = nameof(Daml.Runtime.Data.DamlText);
    public const string DamlBool = nameof(Daml.Runtime.Data.DamlBool);
    public const string DamlUnit = nameof(Daml.Runtime.Data.DamlUnit);
    public const string DamlDate = nameof(Daml.Runtime.Data.DamlDate);
    public const string DamlTimestamp = nameof(Daml.Runtime.Data.DamlTimestamp);
    public const string DamlParty = nameof(Daml.Runtime.Data.DamlParty);
    public const string Identifier = nameof(Daml.Runtime.Data.Identifier);

    public const string DamlContractId = nameof(Daml.Runtime.Contracts.DamlContractId);
    public const string EquatableArray = nameof(Daml.Runtime.Contracts.EquatableArray);
    public const string ContractId = nameof(Daml.Runtime.Contracts.ContractId);
    public const string ITemplate = nameof(Daml.Runtime.Contracts.ITemplate);
    public const string IHasKey = nameof(Daml.Runtime.Contracts.IHasKey<,>);
    public const string IHasChoices = nameof(Daml.Runtime.Contracts.IHasChoices<>);
    public const string IUpgradeable = nameof(Daml.Runtime.Contracts.IUpgradeable);
    public const string TransactionResult = nameof(Daml.Runtime.Contracts.TransactionResult);
    public const string CreatedContract = nameof(Daml.Runtime.Contracts.CreatedContract);
    public const string IDamlInterface = nameof(Daml.Runtime.Contracts.IDamlInterface);
    public const string IHasView = nameof(Daml.Runtime.Contracts.IHasView<object>);
    public const string ViewDescriptor = nameof(Daml.Runtime.Contracts.ViewDescriptor<,>);
    public const string KeyDescriptor = nameof(Daml.Runtime.Contracts.KeyDescriptor<,>);
    public const string ContractKey = nameof(Daml.Runtime.Contracts.ContractKey<>);
    public const string CreatedEvent = nameof(Daml.Runtime.Contracts.CreatedEvent);
    public const string DamlTypeDescriptor = nameof(Daml.Runtime.Contracts.DamlTypeDescriptor);
    public const string DamlTypeKind = nameof(Daml.Runtime.Contracts.DamlTypeKind);

    public const string SubmitterInfo = nameof(Daml.Runtime.Commands.SubmitterInfo);
    public const string ExerciseCommand = nameof(Daml.Runtime.Commands.ExerciseCommand);
    public const string ExerciseByKeyCommand = nameof(Daml.Runtime.Commands.ExerciseByKeyCommand);
    public const string CommandsSubmission = nameof(Daml.Runtime.Commands.CommandsSubmission);
    public const string WorkflowId = nameof(Daml.Runtime.Commands.WorkflowId);
    public const string CommandId = nameof(Daml.Runtime.Commands.CommandId);
    public const string ChoiceName = nameof(Daml.Runtime.Commands.ChoiceName);
    public const string IChoice = nameof(Daml.Runtime.Commands.IChoice);

    public const string ExerciseOutcome = nameof(Daml.Runtime.Outcomes.ExerciseOutcome<object>);

    public const string Tuple2 = nameof(Daml.Runtime.Stdlib.Tuple2<object, object>);
    public const string Tuple3 = nameof(Daml.Runtime.Stdlib.Tuple3<object, object, object>);
    public const string Either = nameof(Daml.Runtime.Stdlib.Either<object, object>);
    public const string Map = nameof(Daml.Runtime.Stdlib.Map<object, object>);
    public const string Set = nameof(Daml.Runtime.Stdlib.Set<object>);
    public const string NonEmpty = nameof(Daml.Runtime.Stdlib.NonEmpty<object>);
    public const string RelTime = nameof(Daml.Runtime.Stdlib.RelTime);
    public const string DayOfWeek = nameof(Daml.Runtime.Stdlib.DayOfWeek);
    public const string GenericStub = nameof(Daml.Runtime.Stdlib.GenericStub);
    public const string Optional = nameof(Daml.Runtime.Stdlib.Optional<object>);

    /// <remarks>
    /// These stay string literals rather than <c>nameof</c>. Each constrains a type parameter to
    /// a runtime interface whose static abstract members make it unusable as a <c>nameof</c> type
    /// argument (CS8920), and no concrete implementation is visible from the codegen project.
    /// </remarks>
    public const string Contract = "Contract";
    public const string IContract = "IContract";
    public const string Choice = "Choice";
    public const string IImplements = "IImplements";

    public const string ILedgerClient = nameof(Daml.Ledger.Abstractions.ILedgerClient);
    public const string ILedgerWriter = nameof(Daml.Ledger.Abstractions.ILedgerWriter);
}
