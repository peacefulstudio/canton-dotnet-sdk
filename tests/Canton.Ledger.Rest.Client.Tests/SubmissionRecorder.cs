// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

internal sealed class SubmissionRecorder : IDisposable
{
    private static readonly Identifier TemplateId = new("pkg", "Module", "Template");
    private static readonly Party Alice = new("party::alice");

    private readonly StubHttpClientFactory _factory;
    private readonly RestLedgerClient _client;

    public SubmissionRecorder()
    {
        Transport = new RecordingHttpHandler()
            .WithResponse(HttpStatusCode.OK, """{"updateId":"u1","completionOffset":"1"}""");
        _factory = new StubHttpClientFactory(Transport);
        _client = new RestLedgerClient(
            _factory,
            Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
    }

    public RecordingHttpHandler Transport { get; }

    public void Dispose() => _factory.Dispose();

    public Task<SubmitAndWaitResult> SubmitCreate(DamlRecord createArguments) =>
        _client.SubmitAndWaitAsync(
            CommandsSubmission.Single(new CreateCommand(TemplateId, createArguments)).WithActAs(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

    public Task<SubmitAndWaitResult> SubmitExercise(DamlValue choiceArgument) =>
        _client.SubmitAndWaitAsync(
            CommandsSubmission.Single(
                    new ExerciseCommand(TemplateId, new ContractId<Payload>("00cid"), new ChoiceName("Accept"), choiceArgument))
                .WithActAs(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

    public async Task<string> CreateArgumentsOnTheWire(DamlRecord createArguments)
    {
        await SubmitCreate(createArguments);
        return LastRequestPayload("CreateCommand", "createArguments");
    }

    public async Task<string> ChoiceArgumentOnTheWire(DamlValue choiceArgument)
    {
        await SubmitExercise(choiceArgument);
        return LastRequestPayload("ExerciseCommand", "choiceArgument");
    }

    private string LastRequestPayload(string commandArm, string payloadProperty)
    {
        using var body = JsonDocument.Parse(Transport.LastRequestBody!, new JsonDocumentOptions { MaxDepth = 256 });
        return body.RootElement
            .GetProperty("commands")[0]
            .GetProperty(commandArm)
            .GetProperty(payloadProperty)
            .GetRawText();
    }

    private sealed class Payload : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, "pkg-name");
    }
}
