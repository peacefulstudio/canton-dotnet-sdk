// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Reads the shipped contract-key corpus DAR and asserts, key by key, that the
/// key shapes the corpus claims to cover are really in the compiled archive.
/// Generated code cannot make that claim: the emitter happily produces a file for
/// a fixture whose key declaration was dropped, so the assertions here read the
/// DAR. The shapes mirror the ones real key-bearing Daml packages use — a record
/// built from several payload fields, a record whose field comes from a nested
/// projection, a record built by a function declared in another module, and a bare
/// <c>Party</c> whose maintainer is the key binder itself.
/// </summary>
public sealed class ContractKeysCorpusDarCharacterizationTests
{
    private const string CorpusLfVersion = "2.3";

    private static async Task<DamlPackage> LoadMainPackageAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "contractkeys.dar");
        var archive = await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
        return archive.MainPackage;
    }

    private static async Task<DamlTemplate> TemplateAsync(string name)
    {
        var package = await LoadMainPackageAsync();
        return package.Modules.SelectMany(m => m.Templates).Single(t => t.Name == name);
    }

    private static async Task<IReadOnlyList<DamlFieldDefinition>> KeyRecordFieldsAsync(string moduleName, string typeName)
    {
        var package = await LoadMainPackageAsync();
        return package.Modules.Single(m => m.Name == moduleName)
            .DataTypes.Single(d => d.Name == typeName)
            .Definition.Should().BeOfType<DamlRecordDefinition>().Subject.Fields;
    }

    [Fact]
    public async Task ContractKeysCorpus_is_pinned_to_the_earliest_lf_version_that_can_express_a_contract_key()
    {
        var package = await LoadMainPackageAsync();

        package.Name.Should().Be("contractkeys-hcc6e8a4f16d9");
        package.LfVersion.Should().Be(
            CorpusLfVersion,
            "conformance/contractkeys/daml.yaml pins --target={0}; damlc rejects a key declaration at 2.1 and 2.2 "
            + "and picks a different version when nothing asks, so a mismatch means the pin stopped taking effect",
            CorpusLfVersion);
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_record_key_built_from_several_payload_fields()
    {
        var account = await TemplateAsync("Account");

        account.Key.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("AccountKey");

        var keyFields = await KeyRecordFieldsAsync("ContractKeys", "AccountKey");
        keyFields.Select(f => f.Name).Should().Equal("custodian", "label");
        keyFields.Single(f => f.Name == "custodian").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        keyFields.Single(f => f.Name == "label").Type.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_record_key_whose_field_comes_from_a_nested_projection()
    {
        var holiday = await TemplateAsync("Holiday");

        holiday.Key.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("HolidayKey");

        var payloadFields = await KeyRecordFieldsAsync("ContractKeys", "Holiday");
        payloadFields.Select(f => f.Name).Should().NotContain(
            "id",
            "the key's id field is projected out of the nested calendar record, so no payload field of that name exists");
        payloadFields.Single(f => f.Name == "calendar").Type
            .Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Calendar");

        var keyFields = await KeyRecordFieldsAsync("ContractKeys", "HolidayKey");
        keyFields.Select(f => f.Name).Should().Equal("provider", "id");
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_record_key_built_by_a_function_in_another_module()
    {
        var schedule = await TemplateAsync("Schedule");

        schedule.Key.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("ScheduleKey");

        var payloadFields = await KeyRecordFieldsAsync("ContractKeys", "Schedule");
        var keyFields = await KeyRecordFieldsAsync("KeyBuilders", "ScheduleKey");

        payloadFields.Select(f => f.Name).Should().Equal("view");
        keyFields.Select(f => f.Name).Should().Equal("custodian", "id");
        keyFields.Select(f => f.Name).Should().NotIntersectWith(
            payloadFields.Select(f => f.Name),
            "the key is built by a helper in another module out of the view's own fields, so no key field shares a payload field name");
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_bare_party_key()
    {
        var steward = await TemplateAsync("Steward");

        steward.Key.Should().Be(
            new DamlPrimitiveType(DamlPrimitive.Party),
            "a key that is not a record at all — its maintainer clause names the key binder itself");
    }

    [Fact]
    public async Task ContractKeysCorpus_carries_a_key_on_every_template_outside_the_Disclosure_module()
    {
        var package = await LoadMainPackageAsync();

        var templates = package.Modules.Where(m => m.Name != "Disclosure").SelectMany(m => m.Templates).ToList();

        templates.Should().HaveCount(
            7,
            "the corpus pins one template per key shape, so a template appearing or vanishing changes what it proves");
        templates.Where(t => t.Key is null).Select(t => t.Name).Should().BeEmpty(
            "this corpus exists to exercise the keyed emission path; a key-less template belongs in the type-shape corpus instead");
    }

    [Fact]
    public async Task ContractKeysCorpus_Disclosure_module_carries_only_the_unkeyed_Offer_with_a_nonconsuming_Inspect_choice()
    {
        var package = await LoadMainPackageAsync();

        var disclosure = package.Modules.Single(m => m.Name == "Disclosure");

        disclosure.Templates.Select(t => t.Name).Should().Equal(
            ["Offer"],
            "the explicit-disclosure live tests need exactly one contract a non-stakeholder can only reach by disclosure");
        var offer = disclosure.Templates.Single();
        offer.Key.Should().BeNull();
        var inspect = offer.Choices.Single(c => c.Name == "Inspect");
        inspect.Consuming.Should().BeFalse();
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_an_interface_with_a_view_and_a_choice_implemented_by_a_keyed_template()
    {
        var package = await LoadMainPackageAsync();

        var stewardship = package.Modules.Single(m => m.Name == "ContractKeys").Interfaces.Single(i => i.Name == "Stewardship");

        stewardship.ViewType.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("StewardshipView");

        var describeCharter = stewardship.Choices.Single(c => c.Name == "DescribeCharter");
        describeCharter.Consuming.Should().BeFalse();
        describeCharter.ReturnType.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));

        var steward = await TemplateAsync("Steward");
        steward.Key.Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        steward.Implements.Should().Contain(r => r.Name == "Stewardship");
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_template_keyed_by_a_tuple()
    {
        var membership = await TemplateAsync("Membership");

        var key = membership.Key.Should().BeOfType<DamlTypeApp>().Subject;
        key.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple2");
        key.Arguments.Should().Equal(
            new DamlPrimitiveType(DamlPrimitive.Party),
            new DamlPrimitiveType(DamlPrimitive.Text));

        var renew = membership.Choices.Single(c => c.Name == "Renew");
        renew.Consuming.Should().BeTrue();
        var renewReturnType = renew.ReturnType.Should().BeOfType<DamlContractIdType>().Subject;
        renewReturnType.Payload.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Membership");
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_template_keyed_by_a_tuple_with_an_optional_component()
    {
        var enrollment = await TemplateAsync("Enrollment");

        var key = enrollment.Key.Should().BeOfType<DamlTypeApp>().Subject;
        key.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple2");
        key.Arguments[0].Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        var note = key.Arguments[1].Should().BeOfType<DamlOptionalType>().Subject;
        note.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));

        var toggle = enrollment.Choices.Single(c => c.Name == "Toggle");
        toggle.Consuming.Should().BeTrue();
        var toggleReturnType = toggle.ReturnType.Should().BeOfType<DamlContractIdType>().Subject;
        toggleReturnType.Payload.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Enrollment");
    }

    [Fact]
    public async Task ContractKeysCorpus_declares_a_template_keyed_by_a_tuple_with_a_nested_optional_component()
    {
        var registration = await TemplateAsync("Registration");

        var key = registration.Key.Should().BeOfType<DamlTypeApp>().Subject;
        key.Base.Should().BeOfType<DamlTypeRef>().Subject.Name.Should().Be("Tuple2");
        key.Arguments[0].Should().Be(new DamlPrimitiveType(DamlPrimitive.Party));
        var tag = key.Arguments[1].Should().BeOfType<DamlOptionalType>().Subject;
        var inner = tag.Value.Should().BeOfType<DamlOptionalType>().Subject;
        inner.Value.Should().Be(new DamlPrimitiveType(DamlPrimitive.Text));
    }
}
