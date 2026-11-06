// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Telemetry;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class LedgerActivityTagNamesTests
{
    [Theory]
    [InlineData(LedgerActivityTagNames.DamlChoice, "daml.choice")]
    [InlineData(LedgerActivityTagNames.DamlContractId, "daml.contract_id")]
    [InlineData(LedgerActivityTagNames.DamlTemplateId, "daml.template_id")]
    [InlineData(LedgerActivityTagNames.DamlPackageId, "daml.package_id")]
    [InlineData(LedgerActivityTagNames.DamlErrorId, "daml.error_id")]
    [InlineData(LedgerActivityTagNames.DamlErrorCategory, "daml.error_category")]
    [InlineData(LedgerActivityTagNames.DamlErrorMetadataPrefix, "daml.error_metadata.")]
    [InlineData(LedgerActivityTagNames.CantonOffset, "canton.offset")]
    [InlineData(LedgerActivityTagNames.CantonFromOffset, "canton.from_offset")]
    [InlineData(LedgerActivityTagNames.CantonSubmitterActAs, "canton.submitter.act_as")]
    [InlineData(LedgerActivityTagNames.CantonSubmitterReadAs, "canton.submitter.read_as")]
    [InlineData(LedgerActivityTagNames.CantonUpdateId, "canton.update_id")]
    [InlineData(LedgerActivityTagNames.CantonPartyId, "canton.party_id")]
    [InlineData(LedgerActivityTagNames.CantonParticipantId, "canton.participant_id")]
    [InlineData(LedgerActivityTagNames.CantonPartyIdHint, "canton.party_id_hint")]
    [InlineData(LedgerActivityTagNames.CantonUserId, "canton.user_id")]
    [InlineData(LedgerActivityTagNames.CantonSubmissionId, "canton.submission_id")]
    [InlineData(LedgerActivityTagNames.CantonTrafficCostBytes, "canton.traffic_cost_bytes")]
    [InlineData(LedgerActivityTagNames.CantonPqsResultCount, "canton.pqs.result_count")]
    [InlineData(LedgerActivityTagNames.RetryAttempt, "retry.attempt")]
    [InlineData(LedgerActivityTagNames.RetryDelayMs, "retry.delay_ms")]
    public void LedgerActivityTagNames_pins_the_canonical_semconv_name(string constant, string canonicalName) =>
        constant.Should().Be(canonicalName);

    [Theory]
    [InlineData("daml.choice")]
    [InlineData("daml.contract_id")]
    [InlineData("daml.template_id")]
    [InlineData("daml.package_id")]
    [InlineData("daml.error_id")]
    [InlineData("daml.error_category")]
    [InlineData("canton.offset")]
    [InlineData("canton.from_offset")]
    [InlineData("canton.submitter.act_as")]
    [InlineData("canton.submitter.read_as")]
    [InlineData("canton.update_id")]
    [InlineData("canton.party_id")]
    [InlineData("canton.participant_id")]
    [InlineData("canton.party_id_hint")]
    [InlineData("canton.user_id")]
    [InlineData("canton.submission_id")]
    [InlineData("canton.traffic_cost_bytes")]
    [InlineData("canton.pqs.result_count")]
    [InlineData("retry.attempt")]
    [InlineData("retry.delay_ms")]
    public void All_carries_every_sdk_owned_attribute_name(string expectedName) =>
        LedgerActivityTagNames.All.Should().Contain(expectedName);

    [Fact]
    public void All_carries_nothing_beyond_the_twenty_attribute_names() =>
        LedgerActivityTagNames.All.Should().HaveCount(20);

    [Fact]
    public void All_leaves_out_the_error_metadata_prefix_because_it_is_not_a_name_on_its_own() =>
        LedgerActivityTagNames.All.Should().NotContain("daml.error_metadata.");

    [Fact]
    public void All_names_are_distinct() =>
        LedgerActivityTagNames.All.Should().OnlyHaveUniqueItems();
}
