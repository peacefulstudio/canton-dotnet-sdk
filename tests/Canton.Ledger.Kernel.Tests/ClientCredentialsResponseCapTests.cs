// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class ClientCredentialsResponseCapTests
{
    private const int CapBytes = 65536;
    private const string TokenJsonPrefix = "{\"access_token\":\"capped-token\",\"expires_in\":3600,\"token_type\":\"Bearer\",\"padding\":\"";

    private static ClientCredentialsProvider CreateProvider(Func<HttpResponseMessage> respond)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("CantonAuth").Returns(_ => new HttpClient(new RespondingHandler(respond)));
        return new ClientCredentialsProvider(
            Options.Create(new ClientCredentialsOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                Domain = "https://auth.example.com",
            }),
            factory,
            TimeProvider.System);
    }

    private static string TokenJsonOfSize(int totalBytes) =>
        TokenJsonPrefix + new string('x', totalBytes - TokenJsonPrefix.Length - 2) + "\"}";

    private static HttpResponseMessage Chunked(HttpStatusCode status, Stream body)
    {
        var content = new StreamContent(body);
        return new HttpResponseMessage(status) { Content = content };
    }

    [Fact]
    public async Task GetTokenAsync_accepts_a_response_of_exactly_65536_bytes()
    {
        var body = TokenJsonOfSize(CapBytes);
        Encoding.UTF8.GetByteCount(body).Should().Be(65536);
        using var provider = CreateProvider(() => Chunked(HttpStatusCode.OK, new NonSeekableStream(Encoding.UTF8.GetBytes(body))));

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("capped-token");
    }

    [Fact]
    public async Task GetTokenAsync_rejects_a_chunked_response_of_65537_bytes()
    {
        var body = TokenJsonOfSize(CapBytes + 1);
        Encoding.UTF8.GetByteCount(body).Should().Be(65537);
        using var provider = CreateProvider(() => Chunked(HttpStatusCode.OK, new NonSeekableStream(Encoding.UTF8.GetBytes(body))));

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*larger than 65536 bytes*");
    }

    [Fact]
    public async Task GetTokenAsync_rejects_an_unending_response_without_reading_it_to_the_end()
    {
        using var provider = CreateProvider(() => Chunked(HttpStatusCode.OK, new UnendingStream()));

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*larger than 65536 bytes*");
    }

    [Fact]
    public async Task GetTokenAsync_rejects_a_response_declaring_65537_bytes_without_reading_its_body()
    {
        using var provider = CreateProvider(() =>
        {
            var response = Chunked(HttpStatusCode.OK, new UnreadableStream());
            response.Content.Headers.ContentLength = CapBytes + 1;
            return response;
        });

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*larger than 65536 bytes*");
    }

    [Fact]
    public async Task GetTokenAsync_reports_the_status_of_a_failing_response_whose_body_is_unending()
    {
        using var provider = CreateProvider(() => Chunked(HttpStatusCode.InternalServerError, new UnendingStream()));

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private sealed class RespondingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond());
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
    }

    private sealed class UnendingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'x', offset, count);
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span.Fill((byte)'x');
            return ValueTask.FromResult(buffer.Length);
        }
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("the body must not be read");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the body must not be read");
    }
}
