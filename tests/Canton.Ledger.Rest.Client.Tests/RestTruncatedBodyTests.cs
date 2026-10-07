// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestTruncatedBodyTests : IDisposable
{
    private static readonly Party Alice = new("party::alice");

    private readonly TruncatedBodyServer _server = new();
    private readonly TruncatedBodyServer _rejectingServer = new(HttpStatusCode.InternalServerError);
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        _server.Dispose();
        _rejectingServer.Dispose();
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    [Fact]
    public async Task A_read_whose_success_body_is_cut_off_reports_NotCommitted_as_no_response()
    {
        var admin = AdminClient();

        var act = () => admin.ListUsersAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.InnerException.Should().BeAssignableTo<IOException>();
    }

    [Fact]
    public async Task An_effect_applied_write_whose_success_body_is_cut_off_reports_Committed_with_the_transport_message()
    {
        var admin = AdminClient();

        var act = () => admin.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.Committed);
        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.Message.Should().StartWith("The response ended prematurely");
        thrown.InnerException.Should().BeAssignableTo<IOException>();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_reports_a_success_body_that_is_cut_off_as_CommittedUndecodable()
    {
        var client = LedgerClient();

        var outcome = await client.TrySubmitAndWaitForTransactionAsync(
            RuntimeCommands.CommandsSubmission
                .Single(RuntimeCommands.CreateCommand.For(new CutOffBodyTemplate()))
                .WithActAs(Alice),
            cancellationToken: TestContext.Current.CancellationToken);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.Message.Should().StartWith("The response ended prematurely");
        undecodable.SourceException.Should().BeAssignableTo<IOException>();
    }

    [Fact]
    public async Task A_read_rejected_with_a_cut_off_error_body_reports_NotCommitted_as_no_response()
    {
        var admin = new RestAdminClient(Factory(_rejectingServer));

        var act = () => admin.ListUsersAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Fact]
    public async Task A_write_rejected_with_a_cut_off_error_body_reports_Unknown_as_no_response()
    {
        var admin = new RestAdminClient(Factory(_rejectingServer));

        var act = () => admin.AllocatePartyAsync("alice", cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    [Fact]
    public async Task GetLedgerEndAsync_whose_success_body_is_cut_off_reports_NotCommitted_as_no_response()
    {
        var client = LedgerClient();

        var act = () => client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.NotCommitted);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
        thrown.InnerException.Should().BeAssignableTo<IOException>();
    }

    [Fact]
    public async Task SubmitAsync_answered_with_success_ignores_a_cut_off_body_and_returns_its_command_id()
    {
        var client = LedgerClient();

        var commandId = await client.SubmitAsync(
            CutOffSubmission().WithCommandId((RuntimeCommands.CommandId)"cmd-cut-off"),
            cancellationToken: TestContext.Current.CancellationToken);

        commandId.Should().Be((RuntimeCommands.CommandId)"cmd-cut-off");
    }

    [Fact]
    public async Task SubmitAsync_rejected_with_a_cut_off_error_body_reports_Unknown_as_no_response()
    {
        var client = new RestLedgerClient(
            Factory(_rejectingServer),
            Options.Create(new RestLedgerClientOptions { HttpAddress = _rejectingServer.Address.ToString() }));

        var act = () => client.SubmitAsync(CutOffSubmission(), cancellationToken: TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.CommitState.Should().Be(CommitState.Unknown);
        thrown.Status.Should().Be(new TransportStatus.NoResponse());
    }

    private static RuntimeCommands.CommandsSubmission CutOffSubmission() =>
        RuntimeCommands.CommandsSubmission
            .Single(RuntimeCommands.CreateCommand.For(new CutOffBodyTemplate()))
            .WithActAs(Alice);

    private StubHttpClientFactory Factory(TruncatedBodyServer? server = null)
    {
        var factory = new StubHttpClientFactory((server ?? _server).CreateHandler());
        _factories.Add(factory);
        return factory;
    }

    private RestAdminClient AdminClient() => new(Factory());

    private RestLedgerClient LedgerClient() =>
        new(Factory(), Options.Create(new RestLedgerClientOptions { HttpAddress = _server.Address.ToString() }));

    private sealed record CutOffBodyTemplate : ITemplate, IDamlRecord<CutOffBodyTemplate>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "Module", "CutOffBodyTemplate");
        public static string PackageId => "pkg";
        public static string PackageName => "pkg-name";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public DamlRecord ToRecord() => new(TemplateId, [new DamlField("owner", Alice.ToDamlValue())]);

        public static DamlRecord __ReadDamlLfJson(
            System.Text.Json.JsonElement json, Daml.Runtime.Serialization.DamlLfJsonDecodeContext context) =>
            TestRecordReader.Read(json, context, ("owner", Daml.Runtime.Serialization.DamlLfJsonDecoders.ReadParty));

        public static CutOffBodyTemplate FromRecord(DamlRecord record) => new();
    }
}
