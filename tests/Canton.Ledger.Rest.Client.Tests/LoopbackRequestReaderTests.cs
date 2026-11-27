// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class LoopbackRequestReaderTests
{
    private const string ChunkedHead = "POST /v2/parties HTTP/1.1\r\nHost: localhost\r\nTransfer-Encoding: chunked\r\n\r\n";

    private static readonly TimeSpan EarlyAnswerWindow = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(ChunkedHead + "2\r\n{}\r\n0\r\n\r\n")]
    [InlineData(ChunkedHead + "2;name=value\r\n{}\r\nA\r\n0123456789\r\n0\r\n\r\n")]
    [InlineData("POST /v2/parties HTTP/1.1\r\nHost: localhost\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("GET /v2/users HTTP/1.1\r\nHost: localhost\r\n\r\n")]
    public async Task ReadWholeRequestAsync_consumes_a_whole_request_to_its_last_byte(string request)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(request));

        var whole = await LoopbackRequestReader.ReadWholeRequestAsync(stream, TestContext.Current.CancellationToken);

        whole.Should().BeTrue();
        stream.Position.Should().Be(stream.Length);
    }

    [Theory]
    [InlineData("POST /v2/parties HTTP/1.1\r\nHost: localhost\r\n")]
    [InlineData(ChunkedHead + "2\r\n{}\r\n")]
    [InlineData(ChunkedHead + "2\r\n{}\r\n0\r\n")]
    [InlineData("POST /v2/parties HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\n{}")]
    public async Task ReadWholeRequestAsync_reports_a_request_the_peer_closed_before_its_end(string request)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(request));

        var whole = await LoopbackRequestReader.ReadWholeRequestAsync(stream, TestContext.Current.CancellationToken);

        whole.Should().BeFalse();
    }

    [Fact]
    public async Task TruncatedBodyServer_answers_only_once_the_chunk_terminator_of_the_request_has_arrived()
    {
        using var server = new TruncatedBodyServer();

        var answeredEarly = await AnswersBeforeTheChunkTerminatorAsync(server.Address);

        answeredEarly.Should().BeFalse();
    }

    [Fact]
    public async Task ScriptedRejectionServer_answers_only_once_the_chunk_terminator_of_the_request_has_arrived()
    {
        using var server = new ScriptedRejectionServer(ScriptedRejectionServer.ChunkedBody(HttpStatusCode.NotFound, "{}"));

        var answeredEarly = await AnswersBeforeTheChunkTerminatorAsync(server.Address);

        answeredEarly.Should().BeFalse();
    }

    private static async Task<bool> AnswersBeforeTheChunkTerminatorAsync(Uri address)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var connection = new TcpClient();
        await connection.ConnectAsync(IPAddress.Loopback, address.Port, cancellationToken);
        var stream = connection.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkedHead + "2\r\n{}\r\n"), cancellationToken);
        var firstResponseByte = stream.ReadAsync(new byte[1], cancellationToken).AsTask();

        var first = await Task.WhenAny(firstResponseByte, Task.Delay(EarlyAnswerWindow, cancellationToken));
        var answeredEarly = first == firstResponseByte;
        await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"), cancellationToken);
        (await firstResponseByte).Should().Be(1);

        return answeredEarly;
    }
}
