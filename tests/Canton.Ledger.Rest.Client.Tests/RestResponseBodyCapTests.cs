// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Wire;
using Canton.Ledger.Testing.Helpers;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestResponseBodyCapTests : IDisposable
{
    private const long Cap = 1024;
    private const string ParticipantIdHead = "{\"participantId\":\"";
    private const string ParticipantIdTail = "\"}";
    private static readonly Party Alice = new("party::alice");
    private static readonly RuntimeCommands.SubmitterInfo AliceSubmitter =
        new(new HashSet<Party> { Alice }, new HashSet<Party>());

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact]
    public void MaxResponseBodyBytes_defaults_to_one_hundred_mebibytes()
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" };

        options.MaxResponseBodyBytes.Should().Be(104857600L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void MaxResponseBodyBytes_that_is_not_positive_fails_validation(long value)
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575", MaxResponseBodyBytes = value };

        var results = options.Validate(new ValidationContext(options)).ToList();

        var failure = results.Should().ContainSingle().Subject;
        failure.ErrorMessage.Should().Be($"MaxResponseBodyBytes must be positive, but was {value}.");
        failure.MemberNames.Should().BeEquivalentTo("MaxResponseBodyBytes");
    }

    [Fact]
    public void MaxResponseBodyBytes_of_one_passes_validation()
    {
        var options = new RestLedgerClientOptions { HttpAddress = "http://localhost:7575", MaxResponseBodyBytes = 1 };

        options.Validate(new ValidationContext(options)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_read_accepts_a_body_of_exactly_MaxResponseBodyBytes()
    {
        var admin = AdminClient(JsonBody(totalBytes: Cap));

        var participantId = await admin.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        participantId.Length.Should().Be(1004);
    }

    [Fact]
    public async Task A_read_rejects_a_body_one_byte_over_the_cap_as_an_undecodable_body()
    {
        var admin = AdminClient(JsonBody(totalBytes: Cap + 1));

        var act = () => admin.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain("MaxResponseBodyBytes").And.Contain("1024");
    }

    [Fact]
    public async Task A_read_rejects_an_endless_body_without_a_content_length_instead_of_buffering_it()
    {
        var served = new ServedBytes();
        var admin = AdminClient(EndlessBody(served));

        var act = () => admin.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        served.Count.Should().BeLessThan(Cap + 65536);
    }

    [Fact]
    public async Task A_read_rejects_a_body_whose_content_length_understates_it()
    {
        var admin = AdminClient(JsonBody(totalBytes: 5000, declaredLength: 100));

        var act = () => admin.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task A_read_rejects_a_declared_content_length_over_the_cap_without_reading_the_body()
    {
        var served = new ServedBytes();
        var admin = AdminClient(EndlessBody(served, declaredLength: 5000));

        var act = () => admin.GetParticipantIdAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        served.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("Read", CommitState.NotCommitted)]
    [InlineData("AcceptedOnlyWrite", CommitState.Unknown)]
    [InlineData("EffectAppliedWrite", CommitState.Committed)]
    public async Task SendAsync_reports_an_over_cap_body_as_undecodable_with_the_commit_state_of_the_calls_kind(
        string kindName, CommitState expected)
    {
        var envelope = Envelope(JsonBody(totalBytes: Cap + 1));
        var call = new RestCall(
            HttpMethod.Post, "/v2/commands/submit", Body: null, "missing body", "malformed prefix: ",
            Enum.Parse<LedgerCallKind>(kindName));

        var act = () => envelope.SendAsync<Raw.GetParticipantIdResponse, string>(
            call, response => response.ParticipantId, timeout: null, TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(expected);
    }

    [Fact]
    public async Task An_effect_applied_write_with_an_over_cap_success_body_reports_Committed_and_an_undecodable_body()
    {
        var admin = AdminClient(JsonBody(
            totalBytes: Cap + 1, head: "{\"partyDetails\":{\"isLocal\":true,\"party\":\"", tail: "\"}}"));

        var act = () => admin.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_an_over_cap_success_body_as_CommittedUndecodable()
    {
        var client = LedgerClient(JsonBody(totalBytes: Cap + 1));

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            RuntimeCommands.CommandsSubmission
                .Single(RuntimeCommands.CreateCommand.For(new CappedTemplate()))
                .WithActAs(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.Message.Should().Contain("MaxResponseBodyBytes");
    }

    [Fact]
    public async Task A_write_rejected_with_an_endless_error_body_still_reports_the_http_status_and_Unknown()
    {
        var served = new ServedBytes();
        var admin = AdminClient(EndlessBody(served), HttpStatusCode.InternalServerError);

        var act = () => admin.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.Http(HttpStatusCode.InternalServerError));
        thrown.CommitState.Should().Be(CommitState.Unknown);
        served.Count.Should().BeLessThan(Cap + 65536);
    }

    [Fact]
    public async Task GetPackageAsync_accepts_an_archive_of_exactly_MaxResponseBodyBytes()
    {
        var admin = AdminClient(OctetBody(totalBytes: Cap), packageHash: "1220aa");

        var archive = await admin.GetPackageAsync("1220bb", TestContext.Current.CancellationToken);

        archive.Payload.Length.Should().Be(1024);
    }

    [Fact]
    public async Task GetPackageAsync_rejects_an_archive_one_byte_over_the_cap_as_an_undecodable_body()
    {
        var admin = AdminClient(OctetBody(totalBytes: Cap + 1), packageHash: "1220aa");

        var act = () => admin.GetPackageAsync("1220bb", TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task A_stream_window_of_exactly_MaxResponseBodyBytes_is_delivered()
    {
        const string checkpoint = """{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "7"}}}}""";
        var client = LedgerClient(PaddedBody("[" + checkpoint + "]", Cap));

        var first = await FirstAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        first.Should().BeOfType<CompletionStreamEvent.Checkpoint>();
    }

    [Fact]
    public async Task A_stream_window_one_byte_over_the_cap_ends_the_stream_with_an_undecodable_body_error()
    {
        const string checkpoint = """{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "7"}}}}""";
        var client = LedgerClient(PaddedBody("[" + checkpoint + "]", Cap + 1));

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.UndecodableBody());
        error.Message.Should().Contain("MaxResponseBodyBytes").And.Contain("1024");
    }

    [Fact]
    public async Task An_endless_stream_window_ends_the_stream_with_an_undecodable_body_error_instead_of_buffering_it()
    {
        var served = new ServedBytes();
        var client = LedgerClient(EndlessBody(served));

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        var error = events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.UndecodableBody());
        served.Count.Should().BeLessThan(Cap + 65536);
    }

    [Fact]
    public async Task A_stream_window_whose_content_length_understates_it_ends_the_stream_with_an_undecodable_body_error()
    {
        var client = LedgerClient(JsonBody(totalBytes: 5000, declaredLength: 100));

        var events = await DrainAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        events.Should().ContainSingle()
            .Which.Should().BeOfType<CompletionStreamEvent.StreamError>()
            .Which.Status.Should().Be(new TransportStatus.UndecodableBody());
    }

    [Fact]
    public async Task An_active_contracts_page_over_the_cap_ends_the_snapshot_with_an_undecodable_body_error()
    {
        var client = LedgerClient(JsonBody(totalBytes: Cap + 1));

        var entries = await DrainAsync(client.SubscribeActiveAsync<CappedTemplate>(
            Alice, LedgerOffset.At(9), cancellationToken: TestContext.Current.CancellationToken));

        var error = entries.Should().ContainSingle()
            .Which.Should().BeOfType<AcsSnapshotEntry<CappedTemplate>.StreamError>().Subject;
        error.Status.Should().Be(new TransportStatus.UndecodableBody());
        entries.OfType<AcsSnapshotEntry<CappedTemplate>.Checkpoint>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_stream_window_larger_than_1024_bytes_is_delivered_when_MaxResponseBodyBytes_is_raised()
    {
        const string checkpoint = """{"completionResponse": {"OffsetCheckpoint": {"value": {"offset": "7"}}}}""";
        var client = LedgerClient(PaddedBody("[" + checkpoint + "]", Cap + 1), maxResponseBodyBytes: 2048);

        var first = await FirstAsync(client.CompletionStreamAsync(
            AliceSubmitter, LedgerOffset.At(5), TestContext.Current.CancellationToken));

        first.Should().BeOfType<CompletionStreamEvent.Checkpoint>();
    }

    private static Func<HttpContent> JsonBody(
        long totalBytes,
        long? declaredLength = null,
        string head = ParticipantIdHead,
        string tail = ParticipantIdTail) =>
        () => new SyntheticContent(
            "application/json", () => new SyntheticStream(head, tail, totalBytes), declaredLength);

    private static Func<HttpContent> OctetBody(long totalBytes) =>
        () => new SyntheticContent("application/octet-stream", () => new SyntheticStream("", "", totalBytes), declaredLength: null);

    private static Func<HttpContent> EndlessBody(ServedBytes served, long? declaredLength = null) =>
        () => new SyntheticContent(
            "application/json",
            () => new SyntheticStream(ParticipantIdHead, "", totalBytes: null, served),
            declaredLength);

    private static Func<HttpContent> PaddedBody(string json, long totalBytes) =>
        () => new SyntheticContent(
            "application/json",
            () => new MemoryStream(Encoding.UTF8.GetBytes(json.PadRight(checked((int)totalBytes)))),
            declaredLength: null);

    private static async Task<TEvent> FirstAsync<TEvent>(IAsyncEnumerable<TEvent> stream)
    {
        await foreach (var streamEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            return streamEvent;
        }

        throw new InvalidOperationException("The stream ended without an event.");
    }

    private static async Task<List<TEvent>> DrainAsync<TEvent>(IAsyncEnumerable<TEvent> stream)
    {
        var events = new List<TEvent>();
        await foreach (var streamEvent in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private RestAdminClient AdminClient(
        Func<HttpContent> body, HttpStatusCode status = HttpStatusCode.OK, string? packageHash = null)
    {
        var options = Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575", MaxResponseBodyBytes = Cap });
        return new RestAdminClient(Factory(body, status, packageHash), logger: null, options);
    }

    private RestCallEnvelope Envelope(Func<HttpContent> body) =>
        new(Factory(body, HttpStatusCode.OK, packageHash: null), NullLogger.Instance, Cap);

    private RestLedgerClient LedgerClient(
        Func<HttpContent> body,
        long maxResponseBodyBytes = Cap)
    {
        var options = Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            MaxResponseBodyBytes = maxResponseBodyBytes,
        });
        return new RestLedgerClient(Factory(body, HttpStatusCode.OK, packageHash: null), options);
    }

    private StubHttpClientFactory Factory(Func<HttpContent> body, HttpStatusCode status, string? packageHash)
    {
        var factory = new StubHttpClientFactory(new BodyHandler(body, status, packageHash));
        _factories.Add(factory);
        return factory;
    }

    private sealed class BodyHandler(Func<HttpContent> body, HttpStatusCode status, string? packageHash)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = body(),
                RequestMessage = request,
            };
            if (packageHash is not null)
            {
                response.Headers.Add("Canton-Package-Hash", packageHash);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ServedBytes
    {
        public long Count;
    }

    private sealed class SyntheticContent(string mediaType, Func<Stream> open, long? declaredLength) : HttpContent
    {
        private readonly string _mediaType = mediaType;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            open().CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = declaredLength.GetValueOrDefault();
            return declaredLength.HasValue;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_mediaType);
            return Task.FromResult(open());
        }
    }

    private sealed class SyntheticStream : Stream
    {
        private readonly byte[] _head;
        private readonly byte[] _tail;
        private readonly long? _totalBytes;
        private readonly ServedBytes? _served;
        private long _position;

        public SyntheticStream(string head, string tail, long? totalBytes, ServedBytes? served = null)
        {
            _head = Encoding.UTF8.GetBytes(head);
            _tail = Encoding.UTF8.GetBytes(tail);
            _totalBytes = totalBytes;
            _served = served;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var produced = 0;
            while (produced < count && (_totalBytes is null || _position < _totalBytes))
            {
                buffer[offset + produced] = ByteAt(_position);
                produced++;
                _position++;
            }

            if (_served is not null)
            {
                _served.Count += produced;
            }

            return produced;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, Math.Min(count, 4096)));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var scratch = new byte[Math.Min(buffer.Length, 4096)];
            var read = Read(scratch, 0, scratch.Length);
            scratch.AsMemory(0, read).CopyTo(buffer);
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private byte ByteAt(long position)
        {
            if (position < _head.Length)
            {
                return _head[position];
            }

            if (_totalBytes is { } total && position >= total - _tail.Length)
            {
                return _tail[position - (total - _tail.Length)];
            }

            return (byte)'a';
        }
    }

    private sealed record CappedTemplate : ITemplate, IDamlRecord<CappedTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "CappedTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context);

        public static CappedTemplate FromRecord(DamlRecord record) => new();
    }
}
