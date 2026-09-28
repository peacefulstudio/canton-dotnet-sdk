// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using IouContract = Iou.Iou;

Console.WriteLine("Daml C# Codegen - Quickstart Example");
Console.WriteLine("=====================================\n");

var alice = new Party("Alice::1220deadbeef");
var bob = new Party("Bob::1220deadbeef");
var charlie = new Party("Charlie::1220deadbeef");

Console.WriteLine("1. Creating an Iou payload:");
var iou = new IouContract(
    Issuer: alice,
    Owner: bob,
    Currency: "USD",
    Amount: 1000.00m
);
Console.WriteLine($"   Issuer: {iou.Issuer}");
Console.WriteLine($"   Owner: {iou.Owner}");
Console.WriteLine($"   Currency: {iou.Currency}");
Console.WriteLine($"   Amount: {iou.Amount}\n");

Console.WriteLine("2. Converting to DamlRecord:");
var record = iou.ToRecord();
Console.WriteLine($"   Fields: {record.Fields.Count}");
foreach (var field in record.Fields)
{
    Console.WriteLine($"   - {field.Label}: {field.Value}");
}
Console.WriteLine();

Console.WriteLine("3. Serializing to JSON and back:");
var json = DamlJsonSerializer.Serialize(record);
Console.WriteLine($"   {json}");
var parsed = DamlJsonSerializer.DeserializeRecord(json);
Console.WriteLine($"   Round-tripped fields: {parsed.Fields.Count}\n");

Console.WriteLine("3b. Building a DamlRecord by hand:");
var manual = DamlRecord.Create(
    DamlField.Create("name", new DamlText("Alice")),
    DamlField.Create("amount", new DamlNumeric(42.5m))
);
Console.WriteLine($"   {DamlJsonSerializer.Serialize(manual)}\n");

Console.WriteLine("4. Building a CreateCommand:");
var createCmd = CreateCommand.For(iou);
Console.WriteLine($"   Template: {createCmd.TemplateId.FullyQualifiedName}");
Console.WriteLine($"   Type: {createCmd.CommandType}\n");

Console.WriteLine("5. Building an ExerciseCommand (Transfer):");
var contractId = new ContractId<IouContract>("00abc123");
var exerciseCmd = ExerciseCommand.For(
    contractId,
    IouContract.ChoiceTransfer.Name,
    new IouContract.Transfer(NewOwner: charlie).ToRecord());
Console.WriteLine($"   Contract: {exerciseCmd.ContractId.Value}");
Console.WriteLine($"   Choice: {exerciseCmd.Choice}");
Console.WriteLine($"   Type: {exerciseCmd.CommandType}\n");

Console.WriteLine("6. Building a command submission:");
var submission = CommandsSubmission.Single(createCmd)
    .WithActAs(alice)
    .WithWorkflowId(new WorkflowId("iou-issuance"))
    .WithCommandId(new CommandId(Guid.NewGuid().ToString()));
Console.WriteLine($"   Commands: {submission.Commands.Count}");
Console.WriteLine($"   ActAs: {string.Join(", ", submission.ActAs ?? [])}");
Console.WriteLine($"   WorkflowId: {submission.WorkflowId}");
Console.WriteLine($"   CommandId: {submission.CommandId}\n");

Console.WriteLine("7. Reconstructing from DamlRecord (as a Ledger API read path would):");
var reconstructed = IouContract.FromRecord(record);
Console.WriteLine($"   Issuer: {reconstructed.Issuer}");
Console.WriteLine($"   Owner: {reconstructed.Owner}");
Console.WriteLine($"   Match: {iou == reconstructed}\n");

Console.WriteLine("8. PQS-style fully qualified template identifier:");
Console.WriteLine($"   {TemplateExtensions.GetTemplateId<IouContract>()}\n");

Console.WriteLine("Done!");
Console.WriteLine();
Console.WriteLine("The code under Generated/ is unmodified `canton-dotnet-sdk` output");
Console.WriteLine("for the Daml model in daml/Iou.daml. Against a live ledger, the generated");
Console.WriteLine("IouSubmissionExtensions.TryCreateAsync and IouExtensions.TryTransferAsync");
Console.WriteLine("extension methods submit these commands through an ILedgerClient.");
