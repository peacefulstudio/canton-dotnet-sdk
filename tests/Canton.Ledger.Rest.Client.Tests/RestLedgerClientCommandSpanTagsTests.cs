// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Microsoft.Extensions.Options;
using Xunit;
using RuntimeCommands = Daml.Runtime.Commands;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestLedgerClientCommandSpanTagsTests : IDisposable
{
    private const string ContractNotFoundBody =
        """
        {
          "code": "CONTRACT_NOT_FOUND",
          "cause": "Contract could not be found",
          "context": { "contract_id": "00feed" },
          "errorCategory": 11,
          "grpcCodeValue": 5
        }
        """;

    private static readonly Party Alice = new("party::alice");

    private readonly RestLedgerClientOptions _options = new() { HttpAddress = "http://localhost:7575" };
    private readonly List<StubHttpClientFactory> _factories = [];

    public void Dispose()
    {
        foreach (var factory in _factories)
        {
            factory.Dispose();
        }
    }

    private RestLedgerClient ClientAnswering(HttpStatusCode status, string body)
    {
        var transport = new RecordingHttpHandler().WithResponse(status, body);
        var factory = new StubHttpClientFactory(new RestActivityHandler { InnerHandler = transport });
        _factories.Add(factory);
        return new RestLedgerClient(factory, Options.Create(_options));
    }

    private static RuntimeCommands.ExerciseCommand ArchiveCommand(string contractId) =>
        new(
            new RuntimeIdentifier("pkg", "Module", "Template"),
            new ContractId<OwnedTemplate>(contractId),
            new RuntimeCommands.ChoiceName("Archive"),
            DamlUnit.Instance);

    private static RuntimeCommands.CommandsSubmission Submission(params RuntimeCommands.ICommand[] commands) =>
        new RuntimeCommands.CommandsSubmission(commands).WithActAs(Alice);

    private static ActivityCapture CaptureSpans() => ActivityCapture.Of(LedgerActivitySourceNames.RestLedgerClient);

    private static IEnumerable<string> ContractIdTags(Activity activity) =>
        activity.TagObjects.Where(tag => tag.Key == "daml.contract_id").Select(tag => (string)tag.Value!);

    private interface IGadget : IDamlType
    {
        static DamlTypeDescriptor IDamlType.DamlTypeId { get; } =
            new(new RuntimeIdentifier("gadget-pkg", "Acme.Gadget", "IGadget"), DamlTypeKind.Interface, "gadget-package");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_tags_the_daml_template_id_and_choice()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.TrySubmitAndWaitForTransactionAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
        activity.GetTagItem("daml.contract_id").Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_tags_the_contract_id_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.TrySubmitAndWaitForTransactionAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        capture.Activities.Should().ContainSingle().Subject.GetTagItem("daml.contract_id").Should().Be("00abc");
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_joins_the_distinct_values_of_a_multi_command_submission()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");
        var submission = Submission(
            RuntimeCommands.CreateCommand.For(new OwnedTemplate()),
            ArchiveCommand("00abc"),
            ArchiveCommand("00def"));

        await client.TrySubmitAndWaitForTransactionAsync(
            submission, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:OwnedTemplate,pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Fact]
    public async Task TryExerciseAsync_tags_the_daml_template_id_and_choice()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Fact]
    public async Task TryCreateAsync_tags_the_daml_template_id_not_the_clr_type_name()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.TryCreateAsync(
            new OwnedTemplate(), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:OwnedTemplate");
        activity.GetTagItem("daml.choice").Should().BeNull();
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionTreeAsync_tags_the_daml_template_id_and_choice()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.TrySubmitAndWaitForTransactionTreeAsync(
            RuntimeCommands.CommandsSubmission.Single(ArchiveCommand("00abc")), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_tags_the_daml_template_id_and_choice()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, """{"updateId":"u-1","completionOffset":1}""");

        await client.SubmitAndWaitAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Fact]
    public async Task SubmitAsync_tags_the_daml_template_id_and_choice()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.SubmitAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAndWaitAsync_tags_the_exercised_contract_id_only_when_opted_in(bool optedIn)
    {
        _options.EmitPartyAndContractSpanTags = optedIn;
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, """{"updateId":"u-1","completionOffset":1}""");

        await client.SubmitAndWaitAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
        ContractIdTags(activity).Should().Equal(optedIn ? ["00abc"] : []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmitAsync_tags_the_exercised_contract_id_only_when_opted_in(bool optedIn)
    {
        _options.EmitPartyAndContractSpanTags = optedIn;
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");

        await client.SubmitAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
        ContractIdTags(activity).Should().Equal(optedIn ? ["00abc"] : []);
    }

    [Fact]
    public async Task TrySubmitAndWaitForTransactionAsync_tags_the_interface_id_of_an_interface_exercise()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, "{}");
        var exercise = RuntimeCommands.ExerciseCommand.For(
            new ContractId<IGadget>("00abc"), new RuntimeCommands.ChoiceName("Poke"), DamlUnit.Instance);

        await client.TrySubmitAndWaitForTransactionAsync(
            Submission(exercise), cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().Be("gadget-pkg:Acme.Gadget:IGadget");
        activity.GetTagItem("daml.choice").Should().Be("Poke");
    }

    [Fact]
    public async Task TryExerciseAsync_tags_the_daml_error_id_and_category_of_a_rejection_but_not_its_metadata_by_default()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.NotFound, ContractNotFoundBody);

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<Daml.Runtime.Outcomes.ExerciseOutcome<DamlUnit>.DamlError>();
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.error_id").Should().Be("CONTRACT_NOT_FOUND");
        activity.GetTagItem("daml.error_category").Should().Be("InvalidGivenCurrentSystemStateResourceMissing");
        activity.GetTagItem("daml.error_metadata.contract_id").Should().BeNull();
        activity.GetTagItem("daml.template_id").Should().Be("pkg:Module:Template");
        activity.GetTagItem("daml.choice").Should().Be("Archive");
    }

    [Fact]
    public async Task TryExerciseAsync_tags_the_daml_error_metadata_when_opted_in()
    {
        _options.EmitPartyAndContractSpanTags = true;
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.NotFound, ContractNotFoundBody);

        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        capture.Activities.Should().ContainSingle().Subject
            .GetTagItem("daml.error_metadata.contract_id").Should().Be("00feed");
    }

    [Fact]
    public async Task TryExerciseAsync_still_reads_the_rejection_after_the_span_tagged_it()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.NotFound, ContractNotFoundBody);

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<Daml.Runtime.Outcomes.ExerciseOutcome<DamlUnit>.DamlError>()
            .Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
    }

    [Fact]
    public async Task SubmitAndWaitAsync_tags_the_daml_error_when_the_participant_rejects()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.NotFound, ContractNotFoundBody);

        var act = () => client.SubmitAndWaitAsync(
            Submission(ArchiveCommand("00abc")), cancellationToken: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<LedgerOperationException>()).Which.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.error_id").Should().Be("CONTRACT_NOT_FOUND");
        activity.GetTagItem("daml.error_category").Should().Be("InvalidGivenCurrentSystemStateResourceMissing");
    }

    [Fact]
    public async Task TryExerciseAsync_leaves_the_daml_error_tags_off_a_span_with_an_unstructured_failure()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.BadGateway, "upstream down");

        await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.error_id").Should().BeNull();
        activity.GetTagItem("daml.error_category").Should().BeNull();
    }

    [Fact]
    public async Task TryExerciseAsync_leaves_the_daml_error_tags_off_when_the_rejection_body_exceeds_the_response_cap()
    {
        _options.MaxResponseBodyBytes = 10;
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.NotFound, ContractNotFoundBody);

        var outcome = await client.TryExerciseAsync<DamlUnit>(
            ArchiveCommand("00abc"), Alice, cancellationToken: TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<Daml.Runtime.Outcomes.ExerciseOutcome<DamlUnit>.InfraError>();
        capture.Activities.Should().ContainSingle().Subject.GetTagItem("daml.error_id").Should().BeNull();
    }

    [Fact]
    public async Task GetLedgerEndAsync_leaves_the_daml_tags_off_a_read_span()
    {
        using var capture = CaptureSpans();
        var client = ClientAnswering(HttpStatusCode.OK, """{"offset":1}""");

        await client.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        var activity = capture.Activities.Should().ContainSingle().Subject;
        activity.GetTagItem("daml.template_id").Should().BeNull();
        activity.GetTagItem("daml.choice").Should().BeNull();
    }
}
