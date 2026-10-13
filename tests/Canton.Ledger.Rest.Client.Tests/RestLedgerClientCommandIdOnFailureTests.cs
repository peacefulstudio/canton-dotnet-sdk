// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCommandIdOnFailureTests : IDisposable
{
    private const string ContractNotFoundBody =
        """
        {
          "code": "CONTRACT_NOT_FOUND",
          "cause": "Contract could not be found",
          "context": {},
          "errorCategory": 11,
          "grpcCodeValue": 5
        }
        """;

    private static readonly Party Alice = new("party::alice");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed class ScriptedHandler(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer();
        }
    }

    private static Func<HttpResponseMessage> Rejection => () =>
        new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(ContractNotFoundBody, Encoding.UTF8, "application/json"),
        };

    private static Func<HttpResponseMessage> LostResponse => () => throw new HttpRequestException("connection reset");

    private RestLedgerClient ClientAnswering(ScriptedHandler transport)
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" };
        var factory = new StubHttpClientFactory(transport);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(options));
    }

    private static RuntimeCommands.CommandsSubmission Create(string? commandId)
    {
        var submission = RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(Alice);
        return commandId is null ? submission : submission.WithCommandId(new RuntimeCommands.CommandId(commandId));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_caller_supplied_command_id_when_the_participant_rejects_it()
    {
        var client = ClientAnswering(new ScriptedHandler(Rejection));

        var act = () => client.SubmitAndWaitAsync(Create("cmd-1"), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId
            .Should().Be(new RuntimeCommands.CommandId("cmd-1"));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_caller_supplied_command_id_when_the_response_is_lost()
    {
        var client = ClientAnswering(new ScriptedHandler(LostResponse));

        var act = () => client.SubmitAndWaitAsync(Create("cmd-2"), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.CommandId.Should().Be(new RuntimeCommands.CommandId("cmd-2"));
    }

    [Fact]
    public async Task SubmitAndWaitAsync_reports_the_command_id_it_generated_when_the_participant_rejects_it()
    {
        var transport = new ScriptedHandler(Rejection);
        var client = ClientAnswering(transport);

        var act = () => client.SubmitAndWaitAsync(Create(commandId: null), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        using var sent = JsonDocument.Parse(transport.Bodies.Single());
        var sentCommandId = sent.RootElement.GetProperty("commandId").GetString();
        sentCommandId.Should().NotBeNullOrEmpty();
        thrown.CommandId.Should().Be(new RuntimeCommands.CommandId(sentCommandId!));
    }

    [Fact]
    public async Task OneOrThrowAsync_after_TrySubmitSingleAsync_reports_the_command_id_the_helper_minted()
    {
        var transport = new ScriptedHandler(LostResponse);
        var client = ClientAnswering(transport);
        var command = new RuntimeCommands.CreateCommand(
            new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, []));

        var act = () => client
            .TrySubmitSingleAsync(command, Alice, cancellationToken: TestContext.Current.CancellationToken)
            .OneOrThrowAsync("Create");

        var thrown = (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which;
        using var sent = JsonDocument.Parse(transport.Bodies.Single());
        var sentCommandId = sent.RootElement.GetProperty("commands").GetProperty("commandId").GetString();
        sentCommandId.Should().NotBeNullOrEmpty();
        thrown.CommandId.Should().Be(new RuntimeCommands.CommandId(sentCommandId!));
    }

    [Fact]
    public async Task SubmitAsync_reports_the_caller_supplied_command_id_when_the_participant_rejects_it()
    {
        var client = ClientAnswering(new ScriptedHandler(Rejection));

        var act = () => client.SubmitAsync(Create("cmd-3"), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId
            .Should().Be(new RuntimeCommands.CommandId("cmd-3"));
    }

    [Fact]
    public async Task GetLedgerEndAsync_reports_no_command_id_when_the_participant_rejects_the_read()
    {
        var client = ClientAnswering(new ScriptedHandler(Rejection));

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowExactlyAsync<LedgerOperationException>()).Which.CommandId.Should().BeNull();
    }
}
