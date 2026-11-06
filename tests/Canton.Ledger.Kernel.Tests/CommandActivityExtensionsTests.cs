// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Telemetry;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public sealed class CommandActivityExtensionsTests : IDisposable
{
    private static readonly Identifier Offer = new("pkg-a", "Market.Offers", "Offer");
    private static readonly Identifier Agreement = new("pkg-b", "Market.Agreements", "Agreement");

    private readonly ActivitySource _source = new(nameof(CommandActivityExtensionsTests));
    private readonly ActivityListener _listener;

    public CommandActivityExtensionsTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == _source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    private Activity NewActivity() => _source.StartActivity("probe")!;

    private static ExerciseCommand Exercise(Identifier template, string contractId, string choice) =>
        new(template, new ContractId<ProbeTemplate>(contractId), new ChoiceName(choice), DamlUnit.Instance);

    private static CommandsSubmission Submit(params ICommand[] commands) => new(commands);

    private static CreateCommand Create(Identifier template) => new(template, new DamlRecord(null, []));

    [Fact]
    public void SetCommandTags_tags_a_create_with_the_template_id_and_no_choice()
    {
        using var activity = NewActivity();

        activity.SetCommandTags(Submit(Create(Offer)), emitIdentifiers: false);

        activity.GetTagItem("daml.template_id").Should().Be("pkg-a:Market.Offers:Offer");
        activity.GetTagItem("daml.choice").Should().BeNull();
        activity.GetTagItem("daml.contract_id").Should().BeNull();
    }

    [Fact]
    public void SetCommandTags_tags_an_exercise_with_the_template_id_and_the_choice()
    {
        using var activity = NewActivity();

        activity.SetCommandTags(
            Submit(Exercise(Offer, "00c1", "Accept")), emitIdentifiers: false);

        activity.GetTagItem("daml.template_id").Should().Be("pkg-a:Market.Offers:Offer");
        activity.GetTagItem("daml.choice").Should().Be("Accept");
    }

    [Fact]
    public void SetCommandTags_leaves_the_contract_id_off_unless_identifiers_are_emitted()
    {
        using var withheld = NewActivity();
        using var emitted = NewActivity();
        var submission = Submit(Exercise(Offer, "00c1", "Accept"));

        withheld.SetCommandTags(submission, emitIdentifiers: false);
        emitted.SetCommandTags(submission, emitIdentifiers: true);

        withheld.GetTagItem("daml.contract_id").Should().BeNull();
        emitted.GetTagItem("daml.contract_id").Should().Be("00c1");
    }

    [Fact]
    public void SetCommandTags_tags_an_exercise_by_key_and_a_create_and_exercise_with_their_choice()
    {
        using var byKey = NewActivity();
        using var createAndExercise = NewActivity();

        byKey.SetCommandTags(
            Submit(new ExerciseByKeyCommand(Offer, DamlUnit.Instance, new ChoiceName("Expire"), DamlUnit.Instance)),
            emitIdentifiers: true);
        createAndExercise.SetCommandTags(
            Submit(new CreateAndExerciseCommand(Agreement, new DamlRecord(null, []), new ChoiceName("Settle"), DamlUnit.Instance)),
            emitIdentifiers: true);

        byKey.GetTagItem("daml.template_id").Should().Be("pkg-a:Market.Offers:Offer");
        byKey.GetTagItem("daml.choice").Should().Be("Expire");
        byKey.GetTagItem("daml.contract_id").Should().BeNull();
        createAndExercise.GetTagItem("daml.template_id").Should().Be("pkg-b:Market.Agreements:Agreement");
        createAndExercise.GetTagItem("daml.choice").Should().Be("Settle");
    }

    [Fact]
    public void SetCommandTags_joins_the_distinct_values_of_a_multi_command_submission_in_first_seen_order()
    {
        using var activity = NewActivity();
        var submission = Submit(
            Exercise(Agreement, "00c1", "Settle"),
            Create(Offer),
            Exercise(Agreement, "00c2", "Settle"),
            Exercise(Offer, "00c3", "Accept"));

        activity.SetCommandTags(submission, emitIdentifiers: true);

        activity.GetTagItem("daml.template_id").Should()
            .Be("pkg-b:Market.Agreements:Agreement,pkg-a:Market.Offers:Offer");
        activity.GetTagItem("daml.choice").Should().Be("Settle,Accept");
        activity.GetTagItem("daml.contract_id").Should().Be("00c1,00c2,00c3");
    }

    [Fact]
    public void SetCommandTags_skips_a_command_it_has_no_description_for()
    {
        using var activity = NewActivity();
        var submission = Submit(new UnknownCommand(), Create(Offer));

        activity.SetCommandTags(submission, emitIdentifiers: false);

        activity.GetTagItem("daml.template_id").Should().Be("pkg-a:Market.Offers:Offer");
    }

    [Fact]
    public void SetCommandTags_tolerates_a_null_activity() =>
        FluentActions.Invoking(() => ((Activity?)null).SetCommandTags(Submit(Create(Offer)), emitIdentifiers: true))
            .Should().NotThrow();

    [Fact]
    public void SetDamlType_tags_the_daml_identifier_of_the_type_not_its_clr_name()
    {
        using var activity = NewActivity();

        activity.SetDamlType<ProbeTemplate>();

        activity.GetTagItem("daml.template_id").Should().Be("pkg-probe:Probe.Module:ProbeEntity");
    }

    [Fact]
    public void SetDamlErrorTags_tags_the_error_id_and_category_by_name()
    {
        using var activity = NewActivity();

        activity.SetDamlErrorTags(
            "CONTRACT_NOT_FOUND",
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            new Dictionary<string, string> { ["contract_id"] = "00c1" },
            emitMetadata: false);

        activity.GetTagItem("daml.error_id").Should().Be("CONTRACT_NOT_FOUND");
        activity.GetTagItem("daml.error_category").Should().Be("InvalidGivenCurrentSystemStateResourceMissing");
        activity.GetTagItem("daml.error_metadata.contract_id").Should().BeNull();
    }

    [Fact]
    public void SetDamlErrorTags_tags_one_attribute_per_metadata_entry_when_metadata_is_emitted()
    {
        using var activity = NewActivity();

        activity.SetDamlErrorTags(
            "LOCAL_VERDICT_LOCKED_CONTRACTS",
            DamlErrorCategory.ContentionOnSharedResources,
            new Dictionary<string, string> { ["category"] = "2", ["agreement_id"] = "agr-1" },
            emitMetadata: true);

        activity.GetTagItem("daml.error_metadata.category").Should().Be("2");
        activity.GetTagItem("daml.error_metadata.agreement_id").Should().Be("agr-1");
    }

    [Fact]
    public void SetDamlErrorTags_leaves_the_error_id_off_when_the_participant_named_none()
    {
        using var activity = NewActivity();

        activity.SetDamlErrorTags(
            string.Empty, DamlErrorCategory.Unknown, new Dictionary<string, string>(), emitMetadata: true);

        activity.GetTagItem("daml.error_id").Should().BeNull();
        activity.GetTagItem("daml.error_category").Should().Be("Unknown");
    }

    [Fact]
    public void SetDamlErrorTags_tags_a_structured_parsed_error()
    {
        using var activity = NewActivity();
        var parsed = new ParsedLedgerError.Structured(
            DamlErrorCategory.InvalidIndependentOfSystemState,
            "INVALID_ARGUMENT",
            "bad",
            new Dictionary<string, string> { ["field"] = "amount" },
            new TransportStatus.Http(HttpStatusCode.BadRequest));

        activity.SetDamlErrorTags(parsed, emitMetadata: true);

        activity.GetTagItem("daml.error_id").Should().Be("INVALID_ARGUMENT");
        activity.GetTagItem("daml.error_category").Should().Be("InvalidIndependentOfSystemState");
        activity.GetTagItem("daml.error_metadata.field").Should().Be("amount");
    }

    [Fact]
    public void SetDamlErrorTags_leaves_an_unstructured_parsed_error_untagged()
    {
        using var activity = NewActivity();
        var parsed = new ParsedLedgerError.Unstructured("down", new TransportStatus.Http(HttpStatusCode.BadGateway));

        activity.SetDamlErrorTags(parsed, emitMetadata: true);

        activity.GetTagItem("daml.error_id").Should().BeNull();
        activity.GetTagItem("daml.error_category").Should().BeNull();
    }

    private sealed record UnknownCommand : ICommand
    {
        public string CommandType => "Unknown";
    }

    private sealed class ProbeTemplate : IDamlType
    {
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(new Identifier("pkg-probe", "Probe.Module", "ProbeEntity"), DamlTypeKind.Template, "probe-package");
    }
}
