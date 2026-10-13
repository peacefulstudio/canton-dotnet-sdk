// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed class RestLedgerClientInteractiveSubmissionTests : IDisposable
{
    [ModuleInitializer]
    internal static void RegisterHandWrittenTemplates()
    {
        GeneratedTypeReaders.ForRecord<TestTemplate>();
    }

    private static readonly Party Alice = new("party::alice");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private sealed record TestTemplate : ITemplate, IDamlRecord<TestTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "InteractiveTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", DamlLfJsonDecoders.ReadParty));

        public static TestTemplate FromRecord(DamlRecord record) => new();
    }

    private RestLedgerClient ClientWith(
        HttpMessageHandler handler, string? userId = "test-user")
    {
        var factory = new StubHttpClientFactory(handler);
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            UserId = userId,
        }));
    }

    private RestLedgerClient ClientServing(string hashBase64, string servedScheme) =>
        ClientWith(
            TransportServing(
                $$"""
                {
                  "preparedTransaction": "",
                  "preparedTransactionHash": "{{hashBase64}}",
                  "hashingSchemeVersion": "{{servedScheme}}"
                }
                """));

    private const string EmptyTransactionV2HashBase64 = "1azcf2AFlvfwPBL/bdpcSMf0eb8fpz3oLs+mLp28YeM=";
    private const string EmptyTransactionV3HashBase64 = "/3luKXR6z3nq01ijFWoYuVcw6LdJJmmhJMt4dS7fU+8=";

    private RestLedgerClient RetryingClientWith(RecordingHttpHandler transport) =>
        ClientWith(new RestRetryHandler(Options.Create(new RestLedgerClientOptions
        {
            HttpAddress = "http://localhost:7575",
            Retry = new RetryOptions { Enabled = true, MaxRetryAttempts = 3, Delay = TimeSpan.Zero },
        }))
        {
            InnerHandler = transport,
        });

    private static RecordingHttpHandler TransportServing(string body) =>
        new RecordingHttpHandler().WithResponse(HttpStatusCode.OK, body);

    private static RecordingHttpHandler TransportFailingAfterSend() =>
        new RecordingHttpHandler()
            .WithTransportException(new HttpRequestException("connection reset after the request was sent"));

    private static void BodyShouldBe(RecordingHttpHandler transport, string expected) =>
        JsonNode.DeepEquals(JsonNode.Parse(transport.LastRequestBody!), JsonNode.Parse(expected))
            .Should().BeTrue($"the request body {transport.LastRequestBody} should equal {expected}");

    private static RuntimeCommands.CommandsSubmission SingleCreateSubmission() =>
        RuntimeCommands.CommandsSubmission.Single(RuntimeCommands.CreateCommand.For(new TestTemplate()), Alice)
            .WithCommandId(new RuntimeCommands.CommandId("cmd-1"));

    private static PreparedSubmission Prepared(HashingSchemeVersion version = HashingSchemeVersion.V2) =>
        new(new byte[] { 0x0A, 0x03 }, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, version, HashingDetails: null, CostEstimate: null);

    private static SignedSubmission Signed(
        HashingSchemeVersion version = HashingSchemeVersion.V2,
        RuntimeCommands.DeduplicationPeriod? deduplicationPeriod = null,
        RuntimeCommands.MinLedgerTime? minLedgerTime = null,
        SignatureFormat format = SignatureFormat.Der,
        SigningAlgorithm algorithm = SigningAlgorithm.EcDsaSha256) =>
        new(
            Prepared(version),
            [
                new PartySignatures(
                    Alice,
                    [new LedgerSignature(format, new byte[] { 0x01, 0x02, 0x03 }, "fingerprint-1", algorithm)]),
            ],
            "sub-1",
            deduplicationPeriod,
            minLedgerTime);

    private const string ServedPreparedBody =
        """
        {
          "preparedTransaction": "",
          "preparedTransactionHash": "/3luKXR6z3nq01ijFWoYuVcw6LdJJmmhJMt4dS7fU+8=",
          "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V3",
          "hashingDetails": "hash details",
          "costEstimation": {
            "estimationTimestamp": "2026-08-15T09:30:00Z",
            "confirmationRequestTrafficCostEstimation": 3000,
            "confirmationResponseTrafficCostEstimation": "1096",
            "totalTrafficCostEstimation": 4096
          }
        }
        """;

    private const string ServedTransactionBody =
        """
        {
          "transaction": {
            "updateId": "upd-1",
            "commandId": "cmd-1",
            "offset": "7",
            "events": [
              {
                "CreatedEvent": {
                  "offset": "7",
                  "contractId": "00holding",
                  "nodeId": 0,
                  "acsDelta": true,
                  "packageName": "pkg-name",
                  "templateId": {"packageId": "pkg", "moduleName": "Module", "entityName": "InteractiveTemplate"},
                  "createArgument": {"owner": "party::alice"}
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task PrepareSubmissionAsync_posts_the_served_prepare_body_without_asking_for_a_cost_estimate()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        await client.PrepareSubmissionAsync(
            SingleCreateSubmission()
                .WithSynchronizerId(new SynchronizerId("sync::ns1"))
                .WithMinLedgerTime(new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(90.5))),
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/interactive-submission/prepare");
        BodyShouldBe(
            transport,
            """
            {
              "userId": "test-user",
              "commandId": "cmd-1",
              "commands": [
                {"CreateCommand": {"templateId": "#pkg-name:Module:InteractiveTemplate", "createArguments": {"owner": "party::alice"}}}
              ],
              "minLedgerTime": {"time": {"MinLedgerTimeRel": {"value": {"seconds": 90, "nanos": 500000000}}}},
              "actAs": ["party::alice"],
              "readAs": [],
              "synchronizerId": "sync::ns1",
              "packageIdSelectionPreference": []
            }
            """);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_sends_the_empty_synchronizer_id_and_package_preference_the_participant_requires()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        await client.PrepareSubmissionAsync(SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("synchronizerId").GetString().Should().Be("");
        body.RootElement.GetProperty("packageIdSelectionPreference").GetRawText().Should().Be("[]");
        body.RootElement.TryGetProperty("estimateTrafficCost", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_nests_an_absolute_min_ledger_time_under_MinLedgerTimeAbs()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        await client.PrepareSubmissionAsync(
            SingleCreateSubmission().WithMinLedgerTime(
                new RuntimeCommands.MinLedgerTime.Absolute(new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero))),
            cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("minLedgerTime").GetRawText()
            .Should().Be("""{"time":{"MinLedgerTimeAbs":{"value":"2026-08-15T09:30:00+00:00"}}}""");
    }

    [Theory]
    [InlineData(EmptyTransactionV2HashBase64, "HASHING_SCHEME_VERSION_V2")]
    [InlineData(EmptyTransactionV3HashBase64, "HASHING_SCHEME_VERSION_V3")]
    public async Task PrepareSubmissionAsync_returns_a_prepared_submission_whose_hash_matches_its_transaction(
        string hashBase64, string servedScheme)
    {
        var client = ClientServing(hashBase64, servedScheme);

        var prepared = await client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        Convert.ToBase64String(prepared.Hash.Span).Should().Be(hashBase64);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_refuses_a_hash_that_does_not_match_its_transaction_as_undecodable()
    {
        var client = ClientServing(EmptyTransactionV3HashBase64, "HASHING_SCHEME_VERSION_V2");

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        var mismatch = thrown.InnerException.Should().BeOfType<PreparedHashMismatchException>().Subject;
        Convert.ToBase64String(mismatch.ReportedHash.Span).Should().Be(EmptyTransactionV3HashBase64);
        Convert.ToBase64String(mismatch.RecomputedHash.Span).Should().Be(EmptyTransactionV2HashBase64);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_refuses_a_scheme_it_cannot_recompute_as_undecodable()
    {
        var client = ClientServing(EmptyTransactionV3HashBase64, "HASHING_SCHEME_VERSION_V4");

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Contain("scheme V4, which this SDK cannot recompute");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_projects_the_base64_transaction_hash_scheme_details_and_cost()
    {
        var client = ClientWith(TransportServing(ServedPreparedBody));

        var prepared = await client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        prepared.PreparedTransaction.ToArray().Should().BeEmpty();
        Convert.ToBase64String(prepared.Hash.Span).Should().Be("/3luKXR6z3nq01ijFWoYuVcw6LdJJmmhJMt4dS7fU+8=");
        prepared.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
        prepared.HashingDetails.Should().Be("hash details");
        prepared.CostEstimate!.EstimatedAt.Should().Be(new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero));
        prepared.CostEstimate.ConfirmationRequestCost.Should().Be(3000L);
        prepared.CostEstimate.ConfirmationResponseCost.Should().Be(1096L);
        prepared.CostEstimate.TotalCost.Should().Be(4096L);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_reports_no_details_and_no_cost_when_the_participant_omits_them()
    {
        var client = ClientWith(TransportServing(
            """
            {
              "preparedTransaction": "",
              "preparedTransactionHash": "1azcf2AFlvfwPBL/bdpcSMf0eb8fpz3oLs+mLp28YeM=",
              "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2",
              "hashingDetails": ""
            }
            """));

        var prepared = await client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        prepared.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V2);
        prepared.HashingDetails.Should().BeNull();
        prepared.CostEstimate.Should().BeNull();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_fails_as_undecodable_on_a_hashing_scheme_this_SDK_does_not_name()
    {
        var client = ClientWith(TransportServing(
            """
            {
              "preparedTransaction": "CgM=",
              "preparedTransactionHash": "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=",
              "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V9"
            }
            """));

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("HASHING_SCHEME_VERSION_V9");
    }

    [Theory]
    [InlineData(HashingSchemeVersion.V2, "HASHING_SCHEME_VERSION_V2")]
    [InlineData(HashingSchemeVersion.V3, "HASHING_SCHEME_VERSION_V3")]
    public async Task PrepareSubmissionAsync_posts_the_requested_hashing_scheme_by_its_served_name(
        HashingSchemeVersion requested, string servedName)
    {
        var transport = TransportServing(ServedPreparedBodyWithScheme(servedName));
        var client = ClientWith(transport);

        var prepared = await client.PrepareSubmissionAsync(
            SingleCreateSubmission(), requested, cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("hashingSchemeVersion").GetString().Should().Be(servedName);
        prepared.HashingSchemeVersion.Should().Be(requested);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_posts_a_requested_V4_by_its_served_name_and_refuses_the_answer_it_cannot_recompute()
    {
        var transport = TransportServing(ServedPreparedBodyWithScheme("HASHING_SCHEME_VERSION_V4"));
        var client = ClientWith(transport);

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), HashingSchemeVersion.V4, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("hashingSchemeVersion").GetString().Should().Be("HASHING_SCHEME_VERSION_V4");
        thrown.InnerException.Should().BeOfType<MalformedResponseException>()
            .Which.Detail.Should().Contain("scheme V4, which this SDK cannot recompute");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_omits_the_hashing_scheme_when_none_is_requested()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        await client.PrepareSubmissionAsync(SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.TryGetProperty("hashingSchemeVersion", out _).Should().BeFalse();
    }

    [Fact]
    public void PrepareSubmissionAsync_rejects_a_requested_hashing_scheme_it_cannot_name_before_any_request()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        Action act = () => _ = client.PrepareSubmissionAsync(
            SingleCreateSubmission(), (HashingSchemeVersion)99, cancellationToken: TestContext.Current.CancellationToken);

        act.Should().Throw<ArgumentException>().WithParameterName("hashingSchemeVersion");
        transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("3q2+7w==")]
    [InlineData("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHw==")]
    [InlineData("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyAh")]
    public async Task PrepareSubmissionAsync_fails_as_undecodable_on_a_hash_that_is_not_32_bytes(string servedHash)
    {
        var client = ClientWith(TransportServing(
            $$"""
            {
              "preparedTransaction": "CgM=",
              "preparedTransactionHash": "{{servedHash}}",
              "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2"
            }
            """));

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain("32 bytes");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_fails_as_undecodable_on_a_scheme_that_differs_from_the_requested_one()
    {
        var client = ClientWith(TransportServing(ServedPreparedBodyWithScheme("HASHING_SCHEME_VERSION_V2")));

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), HashingSchemeVersion.V3, cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Message.Should().Contain("V3").And.Contain("V2");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_accepts_any_scheme_the_participant_chooses_when_none_is_requested()
    {
        var client = ClientWith(TransportServing(ServedPreparedBodyWithScheme("HASHING_SCHEME_VERSION_V3")));

        var prepared = await client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        prepared.HashingSchemeVersion.Should().Be(HashingSchemeVersion.V3);
    }

    private static string ServedPreparedBodyWithScheme(string servedName) =>
        $$"""
        {
          "preparedTransaction": "",
          "preparedTransactionHash": "{{(servedName == "HASHING_SCHEME_VERSION_V2" ? EmptyTransactionV2HashBase64 : EmptyTransactionV3HashBase64)}}",
          "hashingSchemeVersion": "{{servedName}}"
        }
        """;

    [Fact]
    public async Task PrepareSubmissionAsync_fails_as_undecodable_when_the_prepared_transaction_is_missing()
    {
        var client = ClientWith(TransportServing(
            """{"preparedTransactionHash": "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=", "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2"}"""));

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("preparedTransaction");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_is_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.PrepareSubmissionAsync(
            SingleCreateSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_posts_the_signed_submission_with_every_enum_by_its_served_name()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.ExecuteSubmissionAsync(
            Signed(
                deduplicationPeriod: new RuntimeCommands.DeduplicationPeriod.Offset(LedgerOffset.At(4242)),
                minLedgerTime: new RuntimeCommands.MinLedgerTime.Relative(TimeSpan.FromSeconds(90.5))),
            cancellationToken: TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/interactive-submission/execute");
        BodyShouldBe(
            transport,
            """
            {
              "preparedTransaction": "CgM=",
              "partySignatures": {
                "signatures": [
                  {
                    "party": "party::alice",
                    "signatures": [
                      {
                        "format": "SIGNATURE_FORMAT_DER",
                        "signature": "AQID",
                        "signedBy": "fingerprint-1",
                        "signingAlgorithmSpec": "SIGNING_ALGORITHM_SPEC_EC_DSA_SHA_256"
                      }
                    ]
                  }
                ]
              },
              "deduplicationPeriod": {"DeduplicationOffset": {"value": 4242}},
              "submissionId": "sub-1",
              "userId": "test-user",
              "hashingSchemeVersion": "HASHING_SCHEME_VERSION_V2",
              "minLedgerTime": {"time": {"MinLedgerTimeRel": {"value": {"seconds": 90, "nanos": 500000000}}}}
            }
            """);
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_posts_the_V4_hashing_scheme_as_HASHING_SCHEME_VERSION_V4()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.ExecuteSubmissionAsync(
            Signed(version: HashingSchemeVersion.V4), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("hashingSchemeVersion").GetString().Should().Be("HASHING_SCHEME_VERSION_V4");
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_posts_the_ML_DSA_65_algorithm_as_SIGNING_ALGORITHM_SPEC_ML_DSA_65()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.ExecuteSubmissionAsync(
            Signed(algorithm: SigningAlgorithm.MlDsa65), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("partySignatures").GetProperty("signatures")[0].GetProperty("signatures")[0]
            .GetProperty("signingAlgorithmSpec").GetString().Should().Be("SIGNING_ALGORITHM_SPEC_ML_DSA_65");
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_nests_a_duration_deduplication_period_and_omits_unset_optionals()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport, userId: null);

        await client.ExecuteSubmissionAsync(
            Signed(
                version: HashingSchemeVersion.V3,
                deduplicationPeriod: new RuntimeCommands.DeduplicationPeriod.Duration(TimeSpan.FromSeconds(90.5))),
            cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("deduplicationPeriod").GetRawText()
            .Should().Be("""{"DeduplicationDuration":{"value":{"seconds":90,"nanos":500000000}}}""");
        body.RootElement.GetProperty("hashingSchemeVersion").GetString().Should().Be("HASHING_SCHEME_VERSION_V3");
        body.RootElement.TryGetProperty("userId", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("minLedgerTime", out _).Should().BeFalse();
        body.RootElement.TryGetProperty("transactionFormat", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_sends_the_empty_deduplication_arm_the_participant_requires_when_none_is_set()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        await client.ExecuteSubmissionAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("deduplicationPeriod").GetRawText().Should().Be("""{"Empty":{}}""");
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_a_signature_format_the_served_API_has_no_name_for_before_sending()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        var act = () => client.ExecuteSubmissionAsync(
            Signed(format: (SignatureFormat)99), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("SignatureFormat value 99");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_a_signing_algorithm_the_served_API_has_no_name_for_before_sending()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        var act = () => client.ExecuteSubmissionAsync(
            Signed(algorithm: (SigningAlgorithm)99), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("SigningAlgorithm value 99");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_a_hashing_scheme_the_served_API_has_no_name_for_before_sending()
    {
        var transport = TransportServing("{}");
        var client = ClientWith(transport);

        var act = () => client.ExecuteSubmissionAsync(
            Signed(version: (HashingSchemeVersion)7), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("HashingSchemeVersion value 7");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.ExecuteSubmissionAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("4242")]
    [InlineData("\"4242\"")]
    public async Task ExecuteSubmissionAndWaitAsync_projects_the_update_id_and_completion_offset(string wireOffset)
    {
        var transport = TransportServing($$"""{"updateId": "upd-1", "completionOffset": {{wireOffset}}}""");
        var client = ClientWith(transport);

        var executed = await client.ExecuteSubmissionAndWaitAsync(
            Signed(), cancellationToken: TestContext.Current.CancellationToken);

        executed.Should().Be(new ExecutedSubmission("upd-1", LedgerOffset.At(4242)));
        transport.LastRequest!.RequestUri!.PathAndQuery.Should().Be("/v2/interactive-submission/executeAndWait");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("submissionId").GetString().Should().Be("sub-1");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_fails_as_undecodable_when_the_update_id_is_missing()
    {
        var client = ClientWith(TransportServing("""{"completionOffset": 4242}"""));

        var act = () => client.ExecuteSubmissionAndWaitAsync(
            Signed(), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("updateId");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.ExecuteSubmissionAndWaitAsync(
            Signed(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_asks_for_the_submitters_ledger_effects_and_projects_the_transaction()
    {
        var transport = TransportServing(ServedTransactionBody);
        var client = ClientWith(transport);

        var result = await client.ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), new RuntimeCommands.SubmitterInfo(Alice), cancellationToken: TestContext.Current.CancellationToken);

        result.UpdateId.Should().Be("upd-1");
        result.CreatedContracts.Should().ContainSingle().Which.ContractId.Should().Be("00holding");
        transport.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be("/v2/interactive-submission/executeAndWaitForTransaction");
        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        var format = body.RootElement.GetProperty("transactionFormat");
        format.GetProperty("transactionShape").GetString().Should().Be("TRANSACTION_SHAPE_LEDGER_EFFECTS");
        format.GetProperty("eventFormat").GetProperty("filtersByParty").EnumerateObject()
            .Select(party => party.Name).Should().Equal("party::alice");
        body.RootElement.GetProperty("preparedTransaction").GetString().Should().Be("CgM=");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_fails_as_undecodable_when_the_transaction_is_missing()
    {
        var client = ClientWith(TransportServing("{}"));

        var act = () => client.ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), new RuntimeCommands.SubmitterInfo(Alice), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("no transaction");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), new RuntimeCommands.SubmitterInfo(Alice), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_posts_the_vetting_requirements_and_projects_the_references()
    {
        var transport = TransportServing(
            """
            {
              "packageReferences": [{"packageId": "pkg-1", "packageName": "holding", "packageVersion": "1.2.0"}],
              "synchronizerId": "sync::ns1"
            }
            """);
        var client = ClientWith(transport);

        var preferred = await client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice, new Party("party::bob")], "holding")],
            new SynchronizerId("sync::ns1"),
            new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero),
            cancellationToken: TestContext.Current.CancellationToken);

        preferred.PackageReferences.Should().Equal(new PackageReference("pkg-1", "holding", "1.2.0"));
        preferred.SynchronizerId.Should().Be(new SynchronizerId("sync::ns1"));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/interactive-submission/preferred-packages");
        BodyShouldBe(
            transport,
            """
            {
              "packageVettingRequirements": [{"parties": ["party::alice", "party::bob"], "packageName": "holding"}],
              "synchronizerId": "sync::ns1",
              "vettingValidAt": "2026-08-14T09:30:00+00:00"
            }
            """);
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_omits_the_synchronizer_and_validity_instant_when_unset()
    {
        var transport = TransportServing("""{"packageReferences": [], "synchronizerId": "sync::ns1"}""");
        var client = ClientWith(transport);

        await client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "holding")], cancellationToken: TestContext.Current.CancellationToken);

        BodyShouldBe(
            transport,
            """{"packageVettingRequirements": [{"parties": ["party::alice"], "packageName": "holding"}]}""");
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_is_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "holding")], cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_raises_NO_PREFERRED_PACKAGES_FOUND_instead_of_an_empty_result_when_no_version_is_vetted()
    {
        var transport = new RecordingHttpHandler().WithResponse(
            HttpStatusCode.BadRequest,
            """
            {
              "code": "NO_PREFERRED_PACKAGES_FOUND",
              "cause": "No synchronizer satisfies the vetting requirements",
              "correlationId": null,
              "traceId": null,
              "context": {},
              "resources": [],
              "errorCategory": 9,
              "grpcCodeValue": 9,
              "retryInfo": null,
              "definiteAnswer": null
            }
            """);
        var client = ClientWith(transport);

        var act = () => client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "unvetted-pkg")], cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.ErrorId.Should().Be("NO_PREFERRED_PACKAGES_FOUND");
        thrown.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
    }

    [Fact]
    public async Task Interactive_submission_calls_reject_null_arguments()
    {
        var client = ClientWith(TransportServing("{}"));
        var cancellationToken = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => client.PrepareSubmissionAsync(null!, cancellationToken: cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.ExecuteSubmissionAsync(null!, cancellationToken: cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.ExecuteSubmissionAndWaitAsync(null!, cancellationToken: cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.ExecuteSubmissionAndWaitForTransactionAsync(
                null!, new RuntimeCommands.SubmitterInfo(Alice), cancellationToken: cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.GetPreferredPackagesAsync(null!, cancellationToken: cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PrepareSubmissionAsync_sends_the_submissions_package_id_selection_preference()
    {
        var transport = TransportServing(ServedPreparedBody);
        var client = ClientWith(transport);

        await client.PrepareSubmissionAsync(
            SingleCreateSubmission().WithPackageIdSelectionPreference("pkg-b", "pkg-a"),
            cancellationToken: TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("packageIdSelectionPreference").GetRawText().Should().Be("""["pkg-b","pkg-a"]""");
    }
}
