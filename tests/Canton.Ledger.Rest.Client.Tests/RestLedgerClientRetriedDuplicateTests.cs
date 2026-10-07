// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientRetriedDuplicateTests : IDisposable
{
    [ModuleInitializer]
    internal static void RegisterHandWrittenTemplates() => GeneratedTypeReaders.ForRecord<CreatedTemplate>();

    private sealed record CreatedTemplate([property: DamlFieldAttribute("owner")] Party Owner)
        : ITemplate, IDamlRecord<CreatedTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "RetriedTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Owner.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static CreatedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));
    }

    private const string CreatedContractAtOffset9663 =
        """
        {
          "update": {
            "Transaction": {
              "value": {
                "updateId": "upd-9663",
                "commandId": "cmd-1",
                "offset": "9663",
                "events": [
                  {
                    "CreatedEvent": {
                      "offset": "9663",
                      "contractId": "00created",
                      "nodeId": 0,
                      "acsDelta": true,
                      "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "RetriedTemplate"},
                      "createArgument": {"owner": "party::alice"}
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    private const string SubmitPath = "/v2/commands/submit-and-wait-for-transaction";
    private const string UpdateByOffsetPath = "/v2/updates/update-by-offset";

    private static readonly Party Alice = new("party::alice");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var answer = answers[Paths.Count];
            Paths.Add(request.RequestUri!.AbsolutePath);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return answer();
        }
    }

    private static Func<HttpResponseMessage> LostResponse => () => throw new HttpRequestException("connection reset");

    private static Func<HttpResponseMessage> Json(HttpStatusCode statusCode, string body) =>
        () => new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string DuplicateCommandBody(string context) =>
        $$"""
        {
          "code": "DUPLICATE_COMMAND",
          "cause": "A command with the given command id has already been successfully processed",
          "context": {{{context}}},
          "errorCategory": 10,
          "grpcCodeValue": 6
        }
        """;

    private static readonly string AcceptedDuplicate =
        DuplicateCommandBody("""
            "accepted": "true", "completion_offset": "9663", "definite_answer": "true"
            """);

    private const string TransactionAtOffset9663 =
        """
        {
          "update": {
            "Transaction": {
              "value": {
                "updateId": "upd-9663",
                "commandId": "cmd-1",
                "offset": "9663",
                "events": []
              }
            }
          }
        }
        """;

    private static readonly RuntimeCommands.CommandsSubmission Submission =
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("pkg", "Module", "Template"), new DamlRecord(null, [])))
            .WithActAs(Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-1"));

    private RestLedgerClient ClientRetrying(ScriptedHandler transport, bool retryEnabled = true)
    {
        var options = new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Retry = new RetryOptions { Enabled = retryEnabled, MaxRetryAttempts = 1, Delay = TimeSpan.Zero },
        };
        var pipeline = new RestRetryHandler(Options.Create(options)) { InnerHandler = transport };
        var factory = new StubHttpClientFactory(pipeline);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(options));
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_DUPLICATE_COMMAND_to_the_transaction_at_its_completion_offset()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            Json(HttpStatusCode.OK, TransactionAtOffset9663));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>()
            .Which.Result.UpdateId.Should().Be("upd-9663");
        transport.Paths.Should().Equal(SubmitPath, SubmitPath, UpdateByOffsetPath);
        using var pointRead = JsonDocument.Parse(transport.Bodies[2]);
        pointRead.RootElement.GetProperty("offset").GetString().Should().Be("9663");
        pointRead.RootElement.GetProperty("updateFormat").GetProperty("includeTransactions")
            .GetProperty("eventFormat").GetProperty("filtersByParty").EnumerateObject()
            .Select(party => party.Name).Should().Equal("party::alice");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_DUPLICATE_COMMAND_answering_the_first_attempt()
    {
        var transport = new ScriptedHandler(Json(HttpStatusCode.Conflict, AcceptedDuplicate));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        transport.Paths.Should().Equal(SubmitPath);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_without_a_completion_offset()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, DuplicateCommandBody("\"accepted\": \"true\"")));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        transport.Paths.Should().Equal(SubmitPath, SubmitPath);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_the_participant_did_not_accept()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, DuplicateCommandBody(
                "\"accepted\": \"false\", \"completion_offset\": \"9663\"")));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        transport.Paths.Should().Equal(SubmitPath, SubmitPath);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_when_the_point_read_fails()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            Json(HttpStatusCode.NotFound, """{"code": "UPDATE_NOT_FOUND", "cause": "no update", "context": {}, "errorCategory": 11}"""));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        transport.Paths.Should().Equal(SubmitPath, SubmitPath, UpdateByOffsetPath);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_when_the_update_at_the_offset_is_not_a_transaction()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            Json(HttpStatusCode.OK, """{"update": {"Reassignment": {"value": {"offset": "9663", "events": []}}}}"""));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            Submission, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_surfaces_a_caller_cancellation_during_the_point_read()
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            () =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var client = ClientRetrying(transport);

        var act = () => client.TrySubmitAndWaitForTransactionAsync(Submission, cancellationToken: cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_resolves_a_retried_DUPLICATE_COMMAND_to_the_transaction_tree_at_its_completion_offset()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            Json(HttpStatusCode.OK, TransactionAtOffset9663));
        var client = ClientRetrying(transport);

        var outcome = await client.TrySubmitAndWaitForTransactionTreeAsync(
            Submission,
            new RuntimeCommands.SubmitterInfo(new HashSet<Party> { Alice }, new HashSet<Party>()),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionTree>.One>();
        transport.Paths.Should().Equal(SubmitPath, SubmitPath, UpdateByOffsetPath);
    }

    [Fact]
    public async Task TryCreateAsync_resolves_a_retried_DUPLICATE_COMMAND_to_the_contract_created_at_its_completion_offset()
    {
        var transport = new ScriptedHandler(
            LostResponse,
            Json(HttpStatusCode.Conflict, AcceptedDuplicate),
            Json(HttpStatusCode.OK, CreatedContractAtOffset9663));
        var client = ClientRetrying(transport);

        var outcome = await client.TryCreateAsync(
            new CreatedTemplate(Alice),
            new RuntimeCommands.SubmitterInfo(new HashSet<Party> { Alice }, new HashSet<Party>()),
            cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<ContractId<CreatedTemplate>>.One>()
            .Which.Result.Value.Should().Be("00created");
        transport.Paths.Should().Equal(SubmitPath, SubmitPath, UpdateByOffsetPath);
    }
}
