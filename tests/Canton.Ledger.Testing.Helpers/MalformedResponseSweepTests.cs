// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// One table of malformed participant responses that every transport's real client is run against.
/// A throwing call whose response cannot be decoded must raise a <see cref="LedgerOperationException"/>
/// with <see cref="TransportStatus.UndecodableBody"/> and the <see cref="CommitState"/> the call kind
/// decides: a read did not commit, a write whose effect was applied committed, a write that was only
/// accepted may or may not have. Each row names an entry point, the <see cref="MalformedField"/> that
/// is wrong, and that commit state as a literal; a transport subclass builds the wire response in its
/// own encoding. A completeness gate reflects over the throwing unary members of
/// <see cref="ICantonLedgerClient"/> and <see cref="IAdminClient"/> and fails on a member with no row
/// and on a row naming a member the interfaces no longer have, so a new member cannot ship without
/// its malformed-response coverage on both transports.
/// </summary>
public abstract partial class MalformedResponseSweepTests
{
    private static readonly Regex IssueReference = IssueReferencePattern();

    private static readonly MalformedRow[] Table =
    [
        .. Rows("SubmitAndWaitAsync(4)", CommitState.Committed, MalformedField.OffsetNegative, MalformedField.BodyUnreadable),
        .. Rows("GetLedgerEndAsync(2)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.OffsetNonNumeric, MalformedField.BodyUnreadable),
        .. Rows("SubmitAsync(3)", CommitState.Unknown, MalformedField.BodyUnreadable),
        .. Rows("SubmitReassignmentAsync(3)", CommitState.Unknown, MalformedField.BodyUnreadable),
        .. Rows("GetConnectedSynchronizersAsync(4)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GetLedgerApiVersionAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GetUpdateByOffsetAsync(4)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.OffsetNonNumeric, MalformedField.ActingPartyEmpty, MalformedField.CommandIdWhitespace, MalformedField.BodyUnreadable),
        .. Rows("GetUpdateByIdAsync(4)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.OffsetNonNumeric, MalformedField.ActingPartyEmpty, MalformedField.CommandIdWhitespace, MalformedField.BodyUnreadable),
        .. Rows("GetUpdateTreeByOffsetAsync(4)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.OffsetNonNumeric, MalformedField.BodyUnreadable),
        .. Rows("EstimateTrafficCostAsync(3)", CommitState.NotCommitted, MalformedField.CostOutOfRange, MalformedField.TimestampOutOfRange, MalformedField.BodyUnreadable),
        .. Rows("GetContractAsync(4)", CommitState.NotCommitted, MalformedField.ContractIdEmpty, MalformedField.ContractIdWhitespace, MalformedField.ContractIdMissing, MalformedField.TemplateIdMissing, MalformedField.ResultMissing, MalformedField.CreateArgumentFieldMissing, MalformedField.CreateArgumentWrongShape, MalformedField.BodyUnreadable),
        .. Rows("GetEventsByContractIdAsync(4)", CommitState.NotCommitted, MalformedField.ContractIdEmpty, MalformedField.ContractIdWhitespace, MalformedField.ContractIdMissing, MalformedField.TemplateIdMissing, MalformedField.ArchivedContractIdEmpty, MalformedField.OffsetNegative, MalformedField.ArchivedOffsetNegative, MalformedField.CreateArgumentFieldMissing, MalformedField.CreateArgumentWrongShape, MalformedField.BodyUnreadable),
        .. Rows("GetDisclosureAsync(4)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GetActiveContractsPageAsync(7)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.BodyUnreadable),
        .. Rows("GetLatestPrunedOffsetsAsync(2)", CommitState.NotCommitted, MalformedField.OffsetNegative, MalformedField.OffsetNonNumeric, MalformedField.BodyUnreadable),
        .. Rows("GetUpdatesPageAsync(8)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("PrepareSubmissionAsync(3)", CommitState.NotCommitted, MalformedField.ResultMissing, MalformedField.TimestampOutOfRange, MalformedField.BodyUnreadable),
        .. Rows("ExecuteSubmissionAsync(3)", CommitState.Unknown, MalformedField.BodyUnreadable),
        .. Rows("ExecuteSubmissionAndWaitAsync(3)", CommitState.Committed, MalformedField.OffsetNegative, MalformedField.BodyUnreadable),
        .. Rows("ExecuteSubmissionAndWaitForTransactionAsync(4)", CommitState.Committed, MalformedField.ResultMissing, MalformedField.OffsetNegative, MalformedField.ActingPartyEmpty, MalformedField.CommandIdWhitespace, MalformedField.BodyUnreadable),
        .. Rows("GetPreferredPackagesAsync(5)", CommitState.NotCommitted, MalformedField.SynchronizerIdMissing, MalformedField.BodyUnreadable),
        .. Rows("GetPreferredPackageVersionAsync(6)", CommitState.NotCommitted, MalformedField.SynchronizerIdMissing, MalformedField.PackageReferenceMissing, MalformedField.BodyUnreadable),
        .. Rows("GetParticipantIdAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("AllocatePartyAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GenerateExternalPartyTopologyAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("AllocateExternalPartyAsync(2)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GetPartiesAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListKnownPartiesAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("CreateUserAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GetUserAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GrantUserRightsAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("RevokeUserRightsAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("ListUserRightsAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListUsersAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListKnownPackagesAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListPackagesAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GetPackageStatusAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("GetPackageAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListVettedPackagesAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("UploadDarAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("UploadDarAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("ValidateDarAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ValidateDarAsync(3)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("UpdateUserAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("DeleteUserAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("UpdateUserIdentityProviderIdAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("UpdatePartyDetailsAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("UpdatePartyIdentityProviderIdAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GetCommandStatusAsync(4)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("CreateIdentityProviderConfigAsync(2)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GetIdentityProviderConfigAsync(2)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("ListIdentityProviderConfigsAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("UpdateIdentityProviderConfigAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("DeleteIdentityProviderConfigAsync(2)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("UpdateVettedPackagesAsync(6)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("PruneAsync(4)", CommitState.Committed, MalformedField.BodyUnreadable),
        .. Rows("GetTimeAsync(1)", CommitState.NotCommitted, MalformedField.BodyUnreadable),
        .. Rows("SetTimeAsync(3)", CommitState.Committed, MalformedField.BodyUnreadable),
    ];

    /// <summary>
    /// The rows this transport cannot yet satisfy, each with the open issue that tracks the defect,
    /// written as <c>#</c> followed by the issue number. A row listed here must still fail: once the
    /// defect is fixed the row passes and this list must drop it.
    /// </summary>
    protected abstract IReadOnlyDictionary<(string EntryPoint, MalformedField Field), string> KnownDefects { get; }

    /// <summary>
    /// The reason this transport's encoding cannot express <paramref name="field"/> for
    /// <paramref name="entryPoint"/>, or <c>null</c> when it can. A transport that answers a call
    /// without ever reading a field has no wire body that makes that field malformed.
    /// </summary>
    protected abstract string? NotExpressibleOnTheWire(string entryPoint, MalformedField field);

    /// <summary>
    /// Calls <paramref name="entryPoint"/> on this transport's real client against a stubbed
    /// participant whose response is wrong in <paramref name="field"/>.
    /// </summary>
    protected abstract Task InvokeAgainstMalformed(string entryPoint, MalformedField field);

    /// <summary>
    /// Submits through this transport's real client with the non-throwing
    /// <c>TrySubmitAndWaitForTransactionAsync</c>, against a stubbed participant that acknowledged the
    /// command with transaction update id <see cref="DeclaredUpdateId"/> whose payload is wrong in
    /// <paramref name="field"/>.
    /// </summary>
    protected abstract Task<ExerciseOutcome<TransactionResult>> TrySubmitTransactionMalformedIn(MalformedField field);

    /// <summary>
    /// Submits through this transport's real client with the non-throwing
    /// <c>TrySubmitAndWaitForTransactionAsync</c>, against a stubbed participant that acknowledged the
    /// command but answered without a transaction.
    /// </summary>
    protected abstract Task<ExerciseOutcome<TransactionResult>> TrySubmitWithoutTransaction();

    /// <summary>
    /// Runs this transport's point-read wrap over a transaction-shaped response whose projection
    /// throws <paramref name="decodeFailure"/>, and returns whatever escaped the wrap.
    /// </summary>
    protected abstract Exception EscapingPointRead(Exception decodeFailure);

    /// <summary>The update id every transport's stubbed transaction declares.</summary>
    protected const string DeclaredUpdateId = "u-1";

    /// <summary>
    /// The commit state each entry point's malformed response must report, keyed by entry point, for a
    /// transport suite that drives the same entry points with other kinds of malformed response.
    /// </summary>
    public static IReadOnlyDictionary<string, CommitState> CommitStateByEntryPoint { get; } =
        Table.GroupBy(row => row.EntryPoint).ToDictionary(group => group.Key, group => group.First().ExpectedCommitState);

    /// <summary>Every row of the table, as theory data.</summary>
    public static TheoryData<string, MalformedField, CommitState> AllRows()
    {
        var data = new TheoryData<string, MalformedField, CommitState>();
        foreach (var row in Table)
        {
            data.Add(row.EntryPoint, row.Field, row.ExpectedCommitState);
        }

        return data;
    }

    [Fact]
    public void Table_covers_every_throwing_unary_member_of_the_ledger_and_admin_clients()
    {
        var reflected = ThrowingUnaryMembers.KeysOf(typeof(ICantonLedgerClient))
            .Concat(ThrowingUnaryMembers.KeysOf(typeof(IAdminClient)))
            .ToHashSet();
        var covered = Table.Select(row => row.EntryPoint).ToHashSet();

        reflected.Except(covered).Except(ThrowingUnaryMembers.StreamDrained).Should().BeEmpty(
            "a throwing member with no row ships without malformed-response coverage");
        covered.Except(reflected).Should().BeEmpty("a row naming a member the clients no longer have is stale");
        ThrowingUnaryMembers.StreamDrained.Except(reflected).Should().BeEmpty();
    }

    [Fact]
    public void Table_holds_no_row_twice()
    {
        Table.GroupBy(row => (row.EntryPoint, row.Field)).Where(group => group.Count() > 1)
            .Select(group => group.Key).Should().BeEmpty();
    }

    [Fact]
    public void Exclusions_name_only_rows_of_the_table()
    {
        var tableKeys = Table.Select(row => (row.EntryPoint, row.Field)).ToHashSet();

        KnownDefects.Keys.Except(tableKeys).Should().BeEmpty("an exclusion for a row the table lacks is stale");
    }

    [Fact]
    public void Exclusions_name_an_open_issue_by_number()
    {
        KnownDefects.Where(entry => !IssueReference.IsMatch(entry.Value)).Select(entry => entry.Key)
            .Should().BeEmpty("an exclusion without an issue number is a silent gap");
    }

    [Fact]
    public void Known_defects_never_track_a_row_the_wire_cannot_express()
    {
        var expressibleDefects = KnownDefects.Keys
            .Where(key => NotExpressibleOnTheWire(key.EntryPoint, key.Field) is not null);

        expressibleDefects.Should().BeEmpty("a row the wire cannot express has no defect to track");
    }

    [Theory]
    [MemberData(nameof(AllRows))]
    public async Task MalformedResponse_raises_UndecodableBody_with_the_commit_state_of_the_call_kind(
        string entryPoint, MalformedField field, CommitState expected)
    {
        if (IsExcluded(entryPoint, field))
        {
            return;
        }

        var thrown = await ThrownByAsync(entryPoint, field);

        thrown.Status.Should().Be(new TransportStatus.UndecodableBody());
        thrown.CommitState.Should().Be(expected);
    }

    [Fact]
    public async Task KnownDefects_rows_still_fail_until_their_issue_is_fixed()
    {
        var nowPassing = new List<string>();
        foreach (var row in Table.Where(row => KnownDefects.ContainsKey((row.EntryPoint, row.Field))))
        {
            if (await HonoursTheContractAsync(row))
            {
                nowPassing.Add($"{row.EntryPoint}/{row.Field} ({KnownDefects[(row.EntryPoint, row.Field)]})");
            }
        }

        nowPassing.Should().BeEmpty("a row that passes no longer needs its exclusion");
    }

    [Theory]
    [InlineData(MalformedField.OffsetNegative)]
    [InlineData(MalformedField.ActingPartyEmpty)]
    [InlineData(MalformedField.CommandIdWhitespace)]
    public async Task A_committed_transaction_with_a_value_the_Ledger_API_could_not_have_meant_is_reported_as_CommittedUndecodable(
        MalformedField field)
    {
        var outcome = await TrySubmitTransactionMalformedIn(field);

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().Be("u-1");
        undecodable.Message.Should().NotBeNullOrWhiteSpace();
        undecodable.SourceException.Should().NotBeNull();
    }

    [Fact]
    public async Task A_committed_response_without_a_transaction_is_reported_as_CommittedUndecodable_without_an_update_id()
    {
        var outcome = await TrySubmitWithoutTransaction();

        var undecodable = outcome.Should().BeOfType<ExerciseOutcome<TransactionResult>.CommittedUndecodable>().Subject;
        undecodable.UpdateId.Should().BeNull();
        undecodable.Message.Should().NotBeNullOrWhiteSpace();
        undecodable.SourceException.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(RelabelledDecodeFailures))]
    public void The_point_read_wrap_relabels_a_failure_that_says_the_body_was_unreadable(Exception decodeFailure)
    {
        var escaped = EscapingPointRead(decodeFailure);

        escaped.Should().BeOfType<MalformedResponseException>();
        escaped.Message.Should().StartWith("Malformed response from ledger: the transaction at offset 42 could not be decoded: ");
        escaped.InnerException.Should().BeSameAs(decodeFailure);
    }

    public static TheoryData<Exception> RelabelledDecodeFailures() =>
    [
        new MalformedResponseException(
            "CreatedEvent for contract '00aa' has no templateId, "
            + "though the Ledger API marks the field as required."),
        new MalformedTransactionTreeException("Cannot reconstruct the transaction tree: node id 1 follows node id 3."),
        new FormatException("Cannot parse wire Int64 value 'x' as a 64-bit integer."),
    ];

    [Theory]
    [MemberData(nameof(UnrelatedFailures))]
    public void The_point_read_wrap_leaves_a_failure_it_did_not_cause_untouched(Exception unrelated)
    {
        EscapingPointRead(unrelated).Should().BeSameAs(unrelated);
    }

    public static TheoryData<Exception> UnrelatedFailures() =>
    [
        new InvalidOperationException("Transaction contains no exercised event for choice 'Foo'."),
        new InvalidOperationException("Malformed response from ledger: a message wearing the marker without being the type"),
        new ArgumentOutOfRangeException("nodeId"),
        new NotSupportedException("Unknown right kind: KindOneofCase.None"),
        new OperationCanceledException(),
    ];

    [Fact]
    public void The_point_read_wrap_leaves_a_null_dereference_in_the_projection_untouched()
    {
        var escaped = EscapingPointRead(NullDereference());

        escaped.Should().BeOfType<NullReferenceException>();
        escaped.Message.Should().NotContain("Malformed response from ledger: ");
    }

    private static NullReferenceException NullDereference()
    {
        try
        {
            _ = ((object)null!).ToString();
        }
        catch (NullReferenceException nullDereference)
        {
            return nullDereference;
        }

        throw new InvalidOperationException("Dereferencing null did not raise a NullReferenceException.");
    }

    private bool IsExcluded(string entryPoint, MalformedField field) =>
        KnownDefects.ContainsKey((entryPoint, field)) || NotExpressibleOnTheWire(entryPoint, field) is not null;

    private async Task<bool> HonoursTheContractAsync(MalformedRow row)
    {
        try
        {
            var thrown = await ThrownByAsync(row.EntryPoint, row.Field);
            return thrown.Status is TransportStatus.UndecodableBody && thrown.CommitState == row.ExpectedCommitState;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<LedgerOperationException> ThrownByAsync(string entryPoint, MalformedField field)
    {
        var act = () => InvokeAgainstMalformed(entryPoint, field);
        return (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
    }

    private static IEnumerable<MalformedRow> Rows(
        string entryPoint, CommitState expectedCommitState, params MalformedField[] fields) =>
        fields.Select(field => new MalformedRow(entryPoint, field, expectedCommitState));

    [GeneratedRegex(@"^#\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex IssueReferencePattern();

    private sealed record MalformedRow(string EntryPoint, MalformedField Field, CommitState ExpectedCommitState);
}
