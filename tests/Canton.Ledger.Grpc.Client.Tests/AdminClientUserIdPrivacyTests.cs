// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Kernel.Telemetry;
using Canton.Ledger.Testing.Helpers;
using Com.Daml.Ledger.Api.V2.Admin;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Xunit;
using Party = Daml.Runtime.Data.Party;

namespace Canton.Ledger.Grpc.Client.Tests;

[Collection(nameof(AdminClientActivitySourceIsolation))]
public sealed class AdminClientUserIdPrivacyTests : IDisposable
{
    private readonly LedgerClientOptions _options = new() { GrpcAddress = "https://localhost:5001" };
    private readonly GrpcChannel _channel;
    private readonly CapturingLoggerFactory _loggerFactory = new();

    public AdminClientUserIdPrivacyTests()
    {
        _channel = GrpcChannel.ForAddress(_options.GrpcAddress);
    }

    public void Dispose()
    {
        _channel.Dispose();
        _loggerFactory.Dispose();
    }

    private AdminClient CreateClient()
    {
        var invoker = new CannedResponseCallInvoker(AnswerWithAnAcceptedUserAndParty);
        return new AdminClient(
            _options,
            _channel,
            new PartyManagementService.PartyManagementServiceClient(invoker),
            new UserManagementService.UserManagementServiceClient(invoker),
            new StaticTokenProvider("test-token"),
            logger: new Logger<AdminClient>(_loggerFactory));
    }

    private static IMessage AnswerWithAnAcceptedUserAndParty(Type responseType) =>
        responseType == typeof(CreateUserResponse)
            ? new CreateUserResponse { User = new User { Id = "carol-user" } }
            : responseType == typeof(UpdateUserResponse)
                ? new UpdateUserResponse { User = new User { Id = "carol-user" } }
                : responseType == typeof(AllocatePartyResponse)
                    ? new AllocatePartyResponse { PartyDetails = new Com.Daml.Ledger.Api.V2.Admin.PartyDetails { Party = "carol::1220" } }
                    : (IMessage)Activator.CreateInstance(responseType)!;

    private IEnumerable<string> InformationMessages() =>
        _loggerFactory.Records.Where(r => r.Level == LogLevel.Information).Select(r => r.Message);

    private IEnumerable<string> DebugMessages() =>
        _loggerFactory.Records.Where(r => r.Level == LogLevel.Debug).Select(r => r.Message);

    [Fact]
    public async Task AllocatePartyAsync_logs_the_attempt_without_the_hint_by_default()
    {
        await CreateClient().AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Allocating party");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_hint_of_the_attempt_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Allocating party with hint: carol");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_attempt_without_the_user_id_by_default()
    {
        await CreateClient().CreateUserAsync("carol-user", null, cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Creating user");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_user_id_of_the_attempt_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().CreateUserAsync("carol-user", null, cancellationToken: TestContext.Current.CancellationToken);

        DebugMessages().Should().Equal("Creating user: carol-user");
    }

    public static TheoryData<string> OperationsTaggingTheUserId => new()
    {
        "CreateUser",
        "GrantUserRights",
        "RevokeUserRights",
        "ListUserRights",
        "UpdateUser",
        "DeleteUser",
        "UpdateUserIdentityProviderId",
    };

    private static Task Invoke(AdminClient client, string rpcMethod, CancellationToken token) => rpcMethod switch
    {
        "CreateUser" => client.CreateUserAsync("carol-user", new Party("carol::1220"), cancellationToken: token),
        "GrantUserRights" => client.GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], token),
        "RevokeUserRights" => client.RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], token),
        "ListUserRights" => client.ListUserRightsAsync("carol-user", token),
        "UpdateUser" => client.UpdateUserAsync("carol-user", new UserUpdate { IsDeactivated = true }, cancellationToken: token),
        "DeleteUser" => client.DeleteUserAsync("carol-user", cancellationToken: token),
        "UpdateUserIdentityProviderId" => client.UpdateUserIdentityProviderIdAsync("carol-user", null, "idp-2", token),
        _ => throw new ArgumentOutOfRangeException(nameof(rpcMethod), rpcMethod, null),
    };

    [Theory]
    [MemberData(nameof(OperationsTaggingTheUserId))]
    public async Task User_operation_leaves_the_user_id_off_the_span_by_default(string rpcMethod)
    {
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcAdminClient);

        await Invoke(CreateClient(), rpcMethod, TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be(rpcMethod);
        activity.GetTagItem(LedgerActivityTagNames.CantonUserId).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(OperationsTaggingTheUserId))]
    public async Task User_operation_tags_the_user_id_when_opted_in(string rpcMethod)
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = ActivityCapture.Of(LedgerActivitySourceNames.GrpcAdminClient);

        await Invoke(CreateClient(), rpcMethod, TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem(ActivityHelper.RpcMethod).Should().Be(rpcMethod);
        activity.GetTagItem(LedgerActivityTagNames.CantonUserId).Should().Be("carol-user");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_allocation_without_the_party_id_by_default()
    {
        await CreateClient().AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Party allocated");
    }

    [Fact]
    public async Task AllocatePartyAsync_logs_the_party_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().AllocatePartyAsync("carol", cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Party allocated: carol::1220");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_creation_without_the_user_id_by_default()
    {
        await CreateClient().CreateUserAsync("carol-user", null, cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("User created");
    }

    [Fact]
    public async Task CreateUserAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().CreateUserAsync("carol-user", null, cancellationToken: TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("User created: carol-user");
    }

    [Fact]
    public async Task GrantUserRightsAsync_logs_the_grant_without_the_user_id_by_default()
    {
        await CreateClient().GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights granted");
    }

    [Fact]
    public async Task GrantUserRightsAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().GrantUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights granted to user carol-user");
    }

    [Fact]
    public async Task RevokeUserRightsAsync_logs_the_revocation_without_the_user_id_by_default()
    {
        await CreateClient().RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights revoked");
    }

    [Fact]
    public async Task RevokeUserRightsAsync_logs_the_user_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;

        await CreateClient().RevokeUserRightsAsync("carol-user", [new UserRight.ParticipantAdmin()], TestContext.Current.CancellationToken);

        InformationMessages().Should().Equal("Rights revoked from user carol-user");
    }
}
