// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Resilience;
using Canton.Ledger.Rest.Client;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GrpcStatus = Google.Rpc.Status;
using RestClientRegistration = Canton.Ledger.Rest.Client.ServiceCollectionExtensions;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using StatusCode = Grpc.Core.StatusCode;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that a submission whose first attempt commits while its response is lost, and whose retry
/// the participant rejects as <c>DUPLICATE_COMMAND</c>, reaches a consumer holding
/// <see cref="ICantonLedgerClient"/> the same way on both transports: resolved to the committed
/// transaction read at the rejection's <c>completion_offset</c> when the rejection carries one, the
/// <see cref="ExerciseOutcome{T}.DamlError"/> otherwise, and never resolved for a duplicate that
/// answers the first attempt. Each row drives the client resolved from a container against a
/// scripted participant — the gRPC one through <see cref="LedgerClientOptions.ConfigureChannel"/>,
/// the REST one through the named <see cref="HttpClient"/> the registration documents — so the
/// exchange travels the delivery path a deployed consumer uses, without a participant.
/// </summary>
public sealed class LedgerRetriedDuplicateParityTests
{
    private const string Grpc = "gRPC";
    private const string Rest = "REST";

    private const string ParticipantAddress = "http://127.0.0.1:1";
    private const string ParityUser = "parity-user";
    private const string DuplicateCommand = "DUPLICATE_COMMAND";
    private const string SubmitOperation = "SubmitAndWaitForTransaction";
    private const string PointReadOperation = "GetUpdateByOffset";

    private static readonly Party Alice = new("party::alice");

    private static readonly RuntimeCommands.SubmitterInfo Submitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private static readonly IReadOnlyDictionary<string, string> AcceptedAtCompletionOffset =
        new Dictionary<string, string>
        {
            ["accepted"] = "true",
            ["completion_offset"] = "9663",
            ["definite_answer"] = "true",
        };

    public static TheoryData<string> Transports() => [Grpc, Rest];

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task TrySubmitAndWaitForTransactionAsync_resolves_a_retried_DUPLICATE_COMMAND_to_the_transaction_at_its_completion_offset(
        string transport)
    {
        var participant = new Participant(
            FirstAttemptLost: true, AcceptedAtCompletionOffset, PointReadFinds: true);
        await using var lane = Open(transport, participant);

        var outcome = await lane.Capability.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        var resolved = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.One>().Subject;
        resolved.Result.UpdateId.Should().Be("upd-9663");
        resolved.Result.CompletionOffset.Value.Should().Be(9663L);
        participant.Operations.Should().Equal(SubmitOperation, SubmitOperation, PointReadOperation);
        participant.PointReadOffset.Should().Be(9663L);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_DUPLICATE_COMMAND_answering_the_first_attempt(
        string transport)
    {
        var participant = new Participant(
            FirstAttemptLost: false, AcceptedAtCompletionOffset, PointReadFinds: true);
        await using var lane = Open(transport, participant);

        var outcome = await lane.Capability.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        participant.Operations.Should().Equal(SubmitOperation);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_without_a_completion_offset(
        string transport)
    {
        var participant = new Participant(
            FirstAttemptLost: true,
            new Dictionary<string, string> { ["accepted"] = "true" },
            PointReadFinds: true);
        await using var lane = Open(transport, participant);

        var outcome = await lane.Capability.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        participant.Operations.Should().Equal(SubmitOperation, SubmitOperation);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task TrySubmitAndWaitForTransactionAsync_keeps_the_DamlError_of_a_retried_DUPLICATE_COMMAND_when_the_point_read_fails(
        string transport)
    {
        var participant = new Participant(
            FirstAttemptLost: true, AcceptedAtCompletionOffset, PointReadFinds: false);
        await using var lane = Open(transport, participant);

        var outcome = await lane.Capability.TrySubmitAndWaitForTransactionAsync(
            Submission(), Submitter, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.DamlError>()
            .Which.ErrorId.Should().Be("DUPLICATE_COMMAND");
        participant.Operations.Should().Equal(SubmitOperation, SubmitOperation, PointReadOperation);
    }

    private static RuntimeCommands.CommandsSubmission Submission() =>
        RuntimeCommands.CommandsSubmission
            .Single(new RuntimeCommands.CreateCommand(
                new RuntimeIdentifier("test-pkg", "Sample.Foo", "FooBar"),
                new DamlRecord(null, [])))
            .WithCommandId(new RuntimeCommands.CommandId("parity-retried-duplicate-cmd"));

    private static CapabilityLane<ICantonLedgerClient> Open(string transport, Participant participant)
    {
        var services = Register(new ServiceCollection(), transport, participant).BuildServiceProvider();
        return new CapabilityLane<ICantonLedgerClient>(
            services.GetRequiredService<ICantonLedgerClient>(), services.DisposeAsync);
    }

    private static RetryOptions OneImmediateRetry => new()
    {
        Enabled = true,
        MaxRetryAttempts = 1,
        Delay = TimeSpan.Zero,
    };

    private static IServiceCollection Register(
        IServiceCollection services, string transport, Participant participant) => transport switch
    {
        Grpc => services.AddLedgerClient(options =>
        {
            options.GrpcAddress = ParticipantAddress;
            options.UserId = ParityUser;
            options.Retry = OneImmediateRetry;
            options.ConfigureChannel = channel => channel.HttpHandler = new GrpcParticipant(participant);
        }),
        Rest => RegisterRest(services, participant),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown transport."),
    };

    private static IServiceCollection RegisterRest(IServiceCollection services, Participant participant)
    {
        services.AddRestLedgerClient(options =>
        {
            options.HttpAddress = ParticipantAddress;
            options.Retry = OneImmediateRetry;
        });
        services.AddHttpClient(RestClientRegistration.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RestParticipant(participant));
        return services;
    }

    private sealed record Participant(
        bool FirstAttemptLost,
        IReadOnlyDictionary<string, string> DuplicateMetadata,
        bool PointReadFinds)
    {
        public List<string> Operations { get; } = [];

        public long? PointReadOffset { get; set; }

        public bool ReceiveLost(string operation)
        {
            Operations.Add(operation);
            return operation == SubmitOperation && FirstAttemptLost && Operations.Count == 1;
        }
    }

    private sealed class GrpcParticipant(Participant participant) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            var lost = participant.ReceiveLost(operation);
            var requestBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);

            if (lost)
            {
                return Unavailable();
            }

            return operation == SubmitOperation
                ? Rejection(StatusCode.AlreadyExists, DuplicateCommand, participant.DuplicateMetadata)
                : PointRead(requestBody);
        }

        private HttpResponseMessage PointRead(byte[] framedRequest)
        {
            var request = GetUpdateByOffsetRequest.Parser.ParseFrom(framedRequest, 5, framedRequest.Length - 5);
            participant.PointReadOffset = request.Offset;

            return participant.PointReadFinds
                ? Committed(new GetUpdateResponse
                {
                    Transaction = new Transaction { UpdateId = "upd-9663", CommandId = "parity-retried-duplicate-cmd", Offset = request.Offset },
                })
                : Rejection(StatusCode.NotFound, "UPDATE_NOT_FOUND", new Dictionary<string, string>());
        }

        private static HttpResponseMessage Unavailable()
        {
            var response = GrpcResponse(new ByteArrayContent([]));
            response.Headers.TryAddWithoutValidation("grpc-status", "14");
            response.Headers.TryAddWithoutValidation("grpc-message", "connection reset");
            return response;
        }

        private static HttpResponseMessage Committed(IMessage message)
        {
            var body = message.ToByteArray();
            var framed = new byte[5 + body.Length];
            BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(1, 4), body.Length);
            body.CopyTo(framed, 5);

            var response = GrpcResponse(new ByteArrayContent(framed));
            response.TrailingHeaders.Add("grpc-status", "0");
            return response;
        }

        private static HttpResponseMessage Rejection(
            StatusCode statusCode, string errorId, IReadOnlyDictionary<string, string> metadata)
        {
            var errorInfo = new ErrorInfo { Reason = errorId, Domain = "ledger.api" };
            errorInfo.Metadata.Add("category", "10");
            foreach (var (key, value) in metadata)
            {
                errorInfo.Metadata.Add(key, value);
            }

            var status = new GrpcStatus { Code = (int)statusCode, Message = errorId };
            status.Details.Add(Any.Pack(errorInfo));

            var response = GrpcResponse(new ByteArrayContent([]));
            response.Headers.TryAddWithoutValidation("grpc-status", ((int)statusCode).ToString(CultureInfo.InvariantCulture));
            response.Headers.TryAddWithoutValidation("grpc-message", errorId);
            response.Headers.TryAddWithoutValidation(
                "grpc-status-details-bin", Convert.ToBase64String(status.ToByteArray()));
            return response;
        }

        private static HttpResponseMessage GrpcResponse(HttpContent content)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = content,
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            return response;
        }
    }

    private sealed class RestParticipant(Participant participant) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var operation = request.RequestUri!.AbsolutePath.EndsWith("update-by-offset", StringComparison.Ordinal)
                ? PointReadOperation
                : SubmitOperation;
            var lost = participant.ReceiveLost(operation);
            var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            if (lost)
            {
                throw new HttpRequestException("connection reset");
            }

            return operation == SubmitOperation ? Rejection() : PointRead(requestBody);
        }

        private HttpResponseMessage PointRead(string requestBody)
        {
            using var document = JsonDocument.Parse(requestBody);
            participant.PointReadOffset = long.Parse(
                document.RootElement.GetProperty("offset").GetString()!,
                CultureInfo.InvariantCulture);

            return participant.PointReadFinds
                ? Json(
                    HttpStatusCode.OK,
                    """
                    {"update": {"Transaction": {"value": {"updateId": "upd-9663", "commandId": "parity-retried-duplicate-cmd", "offset": "9663", "events": []}}}}
                    """)
                : Json(
                    HttpStatusCode.NotFound,
                    """{"code": "UPDATE_NOT_FOUND", "cause": "no update", "context": {}, "errorCategory": 11}""");
        }

        private HttpResponseMessage Rejection() =>
            Json(
                HttpStatusCode.Conflict,
                $$"""
                {
                  "code": "DUPLICATE_COMMAND",
                  "cause": "A command with the given command id has already been successfully processed",
                  "context": {{JsonSerializer.Serialize(participant.DuplicateMetadata)}},
                  "errorCategory": 10,
                  "grpcCodeValue": 6
                }
                """);

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
            new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
