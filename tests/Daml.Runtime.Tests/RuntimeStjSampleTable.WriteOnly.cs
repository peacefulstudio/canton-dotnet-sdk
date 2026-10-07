// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private const string ContractIdBaseWriteOnly =
        "ContractId is the abstract base of ContractId<T> and its arm set is open, since every template is an arm, so a bare contract-id string cannot name its T and only a reader that knows T can read one; it is written, never read back, and a consumer declares ContractId<T>, which round-trips";

    private const string CommandWriteOnly =
        "commands are built and submitted in memory and never read back, a caller that retries rebuilds the command from ledger state; it is written, never read back, and the DamlValue members it holds have no converter either";

    private const string CommandsSubmissionWriteOnly =
        "a submission wraps the commands a caller builds and submits in memory, so it is written, never read back, and a caller that retries rebuilds it from ledger state; its commands are an IReadOnlyList<ICommand>, an interface with no converter";

    private const string MinLedgerTimeWriteOnly =
        "MinLedgerTime is abstract with a private protected constructor, a closed hierarchy of its two arms, and it is only a member of the CommandsSubmission a caller builds and submits in memory, so a value declared as MinLedgerTime is written, never read back; a caller that retries rebuilds the submission from ledger state, and each arm declared as itself still round-trips";

    private static Dictionary<Type, string> BuildWriteOnly() =>
        new()
        {
            [typeof(ContractId)] = ContractIdBaseWriteOnly,
            [typeof(CreateCommand)] = CommandWriteOnly,
            [typeof(ExerciseCommand)] = CommandWriteOnly,
            [typeof(ExerciseByKeyCommand)] = CommandWriteOnly,
            [typeof(CreateAndExerciseCommand)] = CommandWriteOnly,
            [typeof(CommandsSubmission)] = CommandsSubmissionWriteOnly,
            [typeof(MinLedgerTime)] = MinLedgerTimeWriteOnly,
        };
}
