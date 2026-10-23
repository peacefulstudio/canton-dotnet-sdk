// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime.Data;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class ProductionConstructorLogPrivacyTests : IDisposable
{
    private readonly CapturingLoggerFactory _loggerFactory = new();

    public void Dispose() => _loggerFactory.Dispose();

    private async Task<Exception?> ExceptionLoggedForAnUndecodableCompletion(bool emitIdentifiers)
    {
        var response = new CompletionStreamResponse
        {
            Completion = new Com.Daml.Ledger.Api.V2.Completion { Offset = 7L, UpdateId = "u1" },
        };
        var options = new LedgerClientOptions
        {
            GrpcAddress = "https://localhost:5001",
            UserId = "test-user",
            EmitPartyAndContractSpanTags = emitIdentifiers,
            ConfigureChannel = channelOptions => channelOptions.HttpHandler = new SingleGrpcResponseHandler(response),
        };
        using var channels = new LedgerChannelProvider(Options.Create(options));
        using var client = new LedgerClient(
            Options.Create(options), channels, new StaticTokenProvider("test-token"), new Logger<LedgerClient>(_loggerFactory));

        await foreach (var _ in client.CompletionStreamAsync(
            new RuntimeCommands.SubmitterInfo(new HashSet<Party> { new("party::alice") }),
            cancellationToken: TestContext.Current.CancellationToken))
        {
        }

        return _loggerFactory.Records
            .Single(r => r.Level == LogLevel.Warning
                && r.Message.StartsWith("Completion stream could not decode the completion at offset 7", StringComparison.Ordinal))
            .Exception;
    }

    [Fact]
    public async Task The_shipped_constructor_logs_only_the_exception_type_of_an_undecodable_completion_by_default()
    {
        var exception = await ExceptionLoggedForAnUndecodableCompletion(emitIdentifiers: false);

        exception!.Message.Should().Be("Canton.Ledger.Abstractions.MalformedResponseException");
    }

    [Fact]
    public async Task The_shipped_constructor_logs_the_original_exception_when_party_and_contract_tags_are_opted_in()
    {
        var exception = await ExceptionLoggedForAnUndecodableCompletion(emitIdentifiers: true);

        exception.Should().BeOfType<MalformedResponseException>();
    }

    private sealed class SingleGrpcResponseHandler(IMessage message) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = message.ToByteArray();
            var frame = new byte[5 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)payload.Length);
            payload.CopyTo(frame, 5);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = new Version(2, 0),
                Content = new ByteArrayContent(frame),
            };
            response.Content.Headers.ContentType = new("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return Task.FromResult(response);
        }
    }
}
