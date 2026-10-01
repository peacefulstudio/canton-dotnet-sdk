// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Canton.Ledger.Testing.Tests;

public class FakeInteractiveSubmissionTests
{
    private static readonly Party Alice = new("alice");
    private static readonly SynchronizerId Sync = new("sync-1");

    private static CommandsSubmission Submission() => CommandsSubmission
        .Single(CreateCommand.For(new DemoAsset(Alice, Alice, "GOLD", 1m)))
        .WithActAs(Alice);

    private static PreparedSubmission Prepared() =>
        new(new byte[] { 1, 2 }, new byte[] { 3, 4 }, HashingSchemeVersion.V2, null, null);

    private static SignedSubmission Signed(string submissionId = "sub-1") => new(
        Prepared(),
        [new PartySignatures(Alice, [new LedgerSignature(SignatureFormat.Der, new byte[] { 9 }, "fp", SigningAlgorithm.EcDsaSha256)])],
        submissionId);

    private static PackageReference Package() => new("pkg-id", "pkg-name", "1.0.0");

    [Fact]
    public async Task PrepareSubmissionAsync_returns_the_staged_prepared_submission()
    {
        var prepared = Prepared();
        var client = FakeLedgerClient.Create().WithPreparedSubmission(prepared).Build();

        var result = await client.PrepareSubmissionAsync(Submission(), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(prepared);
    }

    [Fact]
    public async Task PrepareSubmissionAsync_without_a_staged_submission_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.PrepareSubmissionAsync(Submission());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPreparedSubmission").And.Contain("PrepareSubmissionAsync");
    }

    [Fact]
    public async Task PrepareSubmissionAsync_rejects_a_null_submission()
    {
        var client = FakeLedgerClient.Create().WithPreparedSubmission(Prepared()).Build();

        var act = () => client.PrepareSubmissionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_succeeds_without_staging()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_a_null_submission()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAsync_rejects_a_blank_submission_id()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAsync(Signed(" "));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_returns_the_staged_execution()
    {
        var executed = new ExecutedSubmission("update-1", LedgerOffset.At(7));
        var client = FakeLedgerClient.Create().WithExecutedSubmission(executed).Build();

        var result = await client.ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(executed);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_without_a_staged_execution_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAndWaitAsync(Signed());

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithExecutedSubmission").And.Contain("ExecuteSubmissionAndWaitAsync");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_rejects_a_blank_submission_id()
    {
        var client = FakeLedgerClient.Create().WithExecutedSubmission(new ExecutedSubmission("u", LedgerOffset.At(1))).Build();

        var act = () => client.ExecuteSubmissionAndWaitAsync(Signed(""));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitAsync_advances_the_ledger_end_by_one_offset()
    {
        var client = FakeLedgerClient.Create()
            .WithLedgerEnd(LedgerOffset.At(42))
            .WithExecutedSubmission(new ExecutedSubmission("update-1", LedgerOffset.At(43)))
            .Build();

        await client.ExecuteSubmissionAndWaitAsync(Signed(), cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(43));
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_returns_the_staged_transaction()
    {
        var transaction = new Daml.Runtime.Contracts.TransactionResult(
            "update-1", LedgerOffset.At(7), [], [], (CommandId)"cmd-1");
        var client = FakeLedgerClient.Create().WithExecutedTransaction(transaction).Build();

        var result = await client.ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), new SubmitterInfo(Alice), cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(transaction);
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_without_a_staged_transaction_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.ExecuteSubmissionAndWaitForTransactionAsync(Signed(), new SubmitterInfo(Alice));

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithExecutedTransaction").And.Contain("ExecuteSubmissionAndWaitForTransactionAsync");
    }

    [Fact]
    public async Task ExecuteSubmissionAndWaitForTransactionAsync_advances_the_ledger_end_by_one_offset()
    {
        var transaction = new Daml.Runtime.Contracts.TransactionResult(
            "update-1", LedgerOffset.At(7), [], [], (CommandId)"cmd-1");
        var client = FakeLedgerClient.Create().WithLedgerEnd(LedgerOffset.At(10)).WithExecutedTransaction(transaction).Build();

        await client.ExecuteSubmissionAndWaitForTransactionAsync(
            Signed(), new SubmitterInfo(Alice), cancellationToken: TestContext.Current.CancellationToken);

        var end = await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        end.Should().Be(LedgerOffset.At(11));
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_returns_the_staged_preference()
    {
        var preferred = new PreferredPackages([Package()], Sync);
        var client = FakeLedgerClient.Create().WithPreferredPackages(preferred).Build();

        var result = await client.GetPreferredPackagesAsync(
            [new PackageVettingRequirement([Alice], "pkg-name")], cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(preferred);
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_without_a_staged_preference_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetPreferredPackagesAsync([new PackageVettingRequirement([Alice], "pkg-name")]);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPreferredPackages").And.Contain("GetPreferredPackagesAsync");
    }

    [Fact]
    public async Task GetPreferredPackagesAsync_rejects_null_requirements()
    {
        var client = FakeLedgerClient.Create().WithPreferredPackages(new PreferredPackages([], Sync)).Build();

        var act = () => client.GetPreferredPackagesAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_returns_the_staged_preference()
    {
        var preference = new PackagePreference(Package(), Sync);
        var client = FakeLedgerClient.Create().WithPackagePreference(preference).Build();

        var result = await client.GetPreferredPackageVersionAsync([Alice], "pkg-name", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeSameAs(preference);
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_staged_with_no_preference_replays_no_satisfying_package()
    {
        var client = FakeLedgerClient.Create().WithPackagePreference(null).Build();

        var result = await client.GetPreferredPackageVersionAsync([Alice], "pkg-name", cancellationToken: TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_without_a_staged_preference_throws_descriptive_NotSupportedException()
    {
        var client = FakeLedgerClient.Create().Build();

        var act = () => client.GetPreferredPackageVersionAsync([Alice], "pkg-name");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("WithPackagePreference").And.Contain("GetPreferredPackageVersionAsync");
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_rejects_a_blank_package_name()
    {
        var client = FakeLedgerClient.Create().WithPackagePreference(null).Build();

        var act = () => client.GetPreferredPackageVersionAsync([Alice], " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPreferredPackageVersionAsync_rejects_null_parties()
    {
        var client = FakeLedgerClient.Create().WithPackagePreference(null).Build();

        var act = () => client.GetPreferredPackageVersionAsync(null!, "pkg-name");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
