// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Resilience;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientExternalPartyTests : IDisposable
{
    private static readonly SynchronizerId Synchronizer = new("sync::ns1");

    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private RestAdminClient ClientWith(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        _factories.Add(factory);
        return new RestAdminClient(factory);
    }

    private RestAdminClient RetryingClientWith(RecordingHttpHandler transport) =>
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

    private static ExternalPartyTopologyRequest TopologyRequest() =>
        new(
            Synchronizer,
            "alice",
            new SigningPublicKey(PublicKeyFormat.DerX509SubjectPublicKeyInfo, new byte[] { 0x30, 0x59 }, SigningKeySpec.EcP256));

    private static LedgerSignature Signature(byte value) =>
        new(SignatureFormat.Concat, new byte[] { value }, "fingerprint-1", SigningAlgorithm.Ed25519);

    private const string AllocatedAlice = "alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa";

    private const string ServedTopologyBody =
        """
        {
          "partyId": "alice::1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472",
          "publicKeyFingerprint": "1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472",
          "topologyTransactions": ["CoQBCAEQARp+SnwKS2FsaWNlOjoxMjIwYTBjMjhlZDliYWVjZmIyNWIyNGExZTkwMTY5OTc1ZGZhYTIwY2VjNDBiZjkxOTMyM2E1ZDY0NGI1ZTBjOTQ3MhABGhgKFFBBUjo6bG9jYWw6OjEyMjBmZmZmEAIyEQoNEAQaAjBZKgMBBQQwAhABEB4="],
          "multiHash": "EiCReclkGQcYJ97CUmyKTT5JBqteFAwfW8AMRuHmA4gpog=="
        }
        """;

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_posts_the_key_by_served_enum_names_and_omits_proto_defaults()
    {
        var transport = TransportServing(ServedTopologyBody);
        var client = ClientWith(transport);

        await client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/parties/external/generate-topology");
        BodyShouldBe(
            transport,
            """
            {
              "synchronizer": "sync::ns1",
              "partyHint": "alice",
              "publicKey": {
                "format": "CRYPTO_KEY_FORMAT_DER_X509_SUBJECT_PUBLIC_KEY_INFO",
                "keyData": "MFk=",
                "keySpec": "SIGNING_KEY_SPEC_EC_P256"
              }
            }
            """);
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_posts_the_ML_DSA_65_key_spec_as_SIGNING_KEY_SPEC_ML_DSA_65()
    {
        var transport = TransportServing(ServedTopologyBody);
        var client = ClientWith(transport);
        var request = TopologyRequest() with
        {
            PublicKey = new SigningPublicKey(PublicKeyFormat.Raw, new byte[] { 0x30, 0x59 }, SigningKeySpec.MlDsa65),
        };

        await Record.ExceptionAsync(() => client.GenerateExternalPartyTopologyAsync(request, TestContext.Current.CancellationToken));

        using var body = JsonDocument.Parse(transport.LastRequestBody!);
        body.RootElement.GetProperty("publicKey").GetProperty("keySpec").GetString()
            .Should().Be("SIGNING_KEY_SPEC_ML_DSA_65");
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_sends_the_multi_hosting_options_when_set()
    {
        var transport = TransportServing(ServedTopologyBody);
        var client = ClientWith(transport);

        await Record.ExceptionAsync(() => client.GenerateExternalPartyTopologyAsync(
            TopologyRequest() with
            {
                LocalParticipantObservationOnly = true,
                OtherConfirmingParticipantUids = ["participant2::ns2"],
                ConfirmationThreshold = 2,
                ObservingParticipantUids = ["participant3::ns3"],
            },
            TestContext.Current.CancellationToken));

        BodyShouldBe(
            transport,
            """
            {
              "synchronizer": "sync::ns1",
              "partyHint": "alice",
              "publicKey": {
                "format": "CRYPTO_KEY_FORMAT_DER_X509_SUBJECT_PUBLIC_KEY_INFO",
                "keyData": "MFk=",
                "keySpec": "SIGNING_KEY_SPEC_EC_P256"
              },
              "localParticipantObservationOnly": true,
              "otherConfirmingParticipantUids": ["participant2::ns2"],
              "confirmationThreshold": 2,
              "observingParticipantUids": ["participant3::ns3"]
            }
            """);
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_projects_the_party_fingerprint_and_decoded_transactions()
    {
        var client = ClientWith(TransportServing(ServedTopologyBody));

        var topology = await client.GenerateExternalPartyTopologyAsync(
            TopologyRequest(), TestContext.Current.CancellationToken);

        topology.Party.Should().Be(new Party("alice::1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472"));
        topology.PublicKeyFingerprint.Should().Be("1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472");
        topology.TopologyTransactions.Select(transaction => Convert.ToBase64String(transaction.Span))
            .Should().Equal("CoQBCAEQARp+SnwKS2FsaWNlOjoxMjIwYTBjMjhlZDliYWVjZmIyNWIyNGExZTkwMTY5OTc1ZGZhYTIwY2VjNDBiZjkxOTMyM2E1ZDY0NGI1ZTBjOTQ3MhABGhgKFFBBUjo6bG9jYWw6OjEyMjBmZmZmEAIyEQoNEAQaAjBZKgMBBQQwAhABEB4=");
        Convert.ToBase64String(topology.MultiHash.Span).Should().Be("EiCReclkGQcYJ97CUmyKTT5JBqteFAwfW8AMRuHmA4gpog==");
    }

    [Theory]
    [InlineData("1220abcd", "alice::1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472")]
    [InlineData("1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472", "alice::1220abcd")]
    [InlineData("1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472", "alice")]
    public async Task GenerateExternalPartyTopologyAsync_fails_closed_when_the_fingerprint_or_party_namespace_is_not_the_key_sent(
        string fingerprint, string partyId)
    {
        var client = ClientWith(TransportServing(
            $$"""
            {"partyId": "{{partyId}}", "publicKeyFingerprint": "{{fingerprint}}", "topologyTransactions": ["CgE="], "multiHash": "3q2+7w=="}
            """));

        var act = () => client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_fails_closed_when_the_multi_hash_does_not_cover_the_transactions()
    {
        var client = ClientWith(TransportServing(ServedTopologyBody.Replace(
            "EiCReclkGQcYJ97CUmyKTT5JBqteFAwfW8AMRuHmA4gpog==", "EiAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==", StringComparison.Ordinal)));

        var act = () => client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the multi-hash 12200000000000000000000000000000000000000000000000000000000000000000 is not "
            + "12209179c96419071827dec2526c8a4d3e4906ab5e140c1f5bc00c46e1e6038829a2, the hash of the topology transactions returned.");
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_fails_as_undecodable_when_the_multi_hash_is_missing()
    {
        var client = ClientWith(TransportServing(
            """{"partyId": "alice::1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472", "publicKeyFingerprint": "1220a0c28ed9baecfb25b24a1e90169975dfaa20cec40bf919323a5d644b5e0c9472", "topologyTransactions": []}"""));

        var act = () => client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("multiHash");
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_a_key_spec_the_served_API_has_no_name_for_before_sending()
    {
        var transport = TransportServing(ServedTopologyBody);
        var client = ClientWith(transport);

        var act = () => client.GenerateExternalPartyTopologyAsync(
            TopologyRequest() with
            {
                PublicKey = new SigningPublicKey(PublicKeyFormat.Der, new byte[] { 0x30 }, (SigningKeySpec)42),
            },
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("SigningKeySpec value 42");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_a_public_key_format_the_served_API_has_no_name_for_before_sending()
    {
        var transport = TransportServing(ServedTopologyBody);
        var client = ClientWith(transport);

        var act = () => client.GenerateExternalPartyTopologyAsync(
            TopologyRequest() with
            {
                PublicKey = new SigningPublicKey((PublicKeyFormat)99, new byte[] { 0x30 }, SigningKeySpec.EcP256),
            },
            TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Message.Should().Contain("PublicKeyFormat value 99");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_rejects_a_confirmation_threshold_beyond_the_served_int32()
    {
        var client = ClientWith(TransportServing(ServedTopologyBody));

        var act = () => client.GenerateExternalPartyTopologyAsync(
            TopologyRequest() with { ConfirmationThreshold = 2_147_483_648u },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GenerateExternalPartyTopologyAsync_is_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.GenerateExternalPartyTopologyAsync(TopologyRequest(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<LedgerOperationException>();
        transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_posts_the_signed_onboarding_transactions_and_projects_the_party()
    {
        var transport = TransportServing("""{"partyId": "alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"}""");
        var client = ClientWith(transport);

        var party = await client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(
                Synchronizer,
                [new SignedTopologyTransaction(GoldenExternalPartyOnboarding.Transaction(), [Signature(0x07)])],
                [Signature(0x08)],
                WaitForAllocation: true,
                IdentityProviderId: "idp-1",
                UserId: "alice-user"),
            TestContext.Current.CancellationToken);

        party.Should().Be(new Party(AllocatedAlice));
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.RequestUri!.PathAndQuery.Should().Be("/v2/parties/external/allocate");
        BodyShouldBe(
            transport,
            """
            {
              "synchronizer": "sync::ns1",
              "onboardingTransactions": [
                {
                  "transaction": "CrcBCAEQARqwAUqtAQpLYWxpY2U6OjEyMjAyMDUwNTdlMzMxY2M4OTI5ZGQyMTdlMmY4ZTYzZjUwM2I3MDgxNzczZGU2MGQwMWZiNDY4Mzk3MDBiYzVjYWFhEAEaHwobUEFSOjpwYXJ0aWNpcGFudDE6OjEyMjBhYWFhEAIyOwo3EAQaLDAqMAUGAytlcAMhANkcFIiB1VFXbpccw/DSrT5iGmgajHq9QPdKw46JJST2KgMBBQQwARABEB4=",
                  "signatures": [
                    {
                      "format": "SIGNATURE_FORMAT_CONCAT",
                      "signature": "Bw==",
                      "signedBy": "fingerprint-1",
                      "signingAlgorithmSpec": "SIGNING_ALGORITHM_SPEC_ED25519"
                    }
                  ]
                }
              ],
              "multiHashSignatures": [
                {
                  "format": "SIGNATURE_FORMAT_CONCAT",
                  "signature": "CA==",
                  "signedBy": "fingerprint-1",
                  "signingAlgorithmSpec": "SIGNING_ALGORITHM_SPEC_ED25519"
                }
              ],
              "identityProviderId": "idp-1",
              "waitForAllocation": true,
              "userId": "alice-user"
            }
            """);
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_omits_the_multi_hash_signatures_and_unset_optionals()
    {
        var transport = TransportServing("""{"partyId": "alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"}""");
        var client = ClientWith(transport);

        await client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Synchronizer, [GoldenExternalPartyOnboarding.UnsignedTransaction()], []),
            TestContext.Current.CancellationToken);

        BodyShouldBe(
            transport,
            """{"synchronizer": "sync::ns1", "onboardingTransactions": [{"transaction": "CrcBCAEQARqwAUqtAQpLYWxpY2U6OjEyMjAyMDUwNTdlMzMxY2M4OTI5ZGQyMTdlMmY4ZTYzZjUwM2I3MDgxNzczZGU2MGQwMWZiNDY4Mzk3MDBiYzVjYWFhEAEaHwobUEFSOjpwYXJ0aWNpcGFudDE6OjEyMjBhYWFhEAIyOwo3EAQaLDAqMAUGAytlcAMhANkcFIiB1VFXbpccw/DSrT5iGmgajHq9QPdKw46JJST2KgMBBQQwARABEB4="}]}""");
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_fails_as_undecodable_when_the_party_id_is_missing()
    {
        var client = ClientWith(TransportServing("{}"));

        var act = () => client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Synchronizer, [GoldenExternalPartyOnboarding.UnsignedTransaction()], []), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("partyId");
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_fails_as_undecodable_and_committed_when_another_party_is_allocated()
    {
        var client = ClientWith(TransportServing("""{"partyId": "mallory::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"}"""));

        var act = () => client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Synchronizer, [GoldenExternalPartyOnboarding.UnsignedTransaction()], []), TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.InnerException.Should().BeOfType<MalformedResponseException>().Which.Detail.Should().Be(
            "the participant allocated party 'mallory::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa', but the onboarding topology was generated for 'alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa'.");
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_rejects_onboarding_transactions_that_are_not_a_party_topology_before_sending()
    {
        var transport = TransportServing("""{"partyId": "alice::1220205057e331cc8929dd217e2f8e63f503b7081773de60d01fb46839700bc5caaa"}""");
        var client = ClientWith(transport);

        var act = () => client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Synchronizer, [], []), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("allocation");
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AllocateExternalPartyAsync_is_never_replayed_by_the_opt_in_retry_pipeline()
    {
        var transport = TransportFailingAfterSend();
        var client = RetryingClientWith(transport);

        var act = () => client.AllocateExternalPartyAsync(
            new ExternalPartyAllocation(Synchronizer, [GoldenExternalPartyOnboarding.UnsignedTransaction()], []), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Status.Should().Be(new TransportStatus.NoResponse());
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task External_party_calls_reject_null_arguments()
    {
        var client = ClientWith(TransportServing("{}"));
        var cancellationToken = TestContext.Current.CancellationToken;

        await FluentActions.Awaiting(() => client.GenerateExternalPartyTopologyAsync(null!, cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => client.AllocateExternalPartyAsync(null!, cancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }
}
