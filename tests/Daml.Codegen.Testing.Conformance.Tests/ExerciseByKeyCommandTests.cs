// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The by-key command builders are the key type's reason to exist on the emitted
/// surface: they address a contract the caller has never fetched. These tests pin the
/// command they build on the corpus's own generated types, across a create-bearing
/// choice, a value-returning choice, and the synthesised Archive.
/// </summary>
public class ExerciseByKeyCommandTests
{
    private static readonly Party Custodian = new("custodian::1220");
    private static readonly AccountKey Key = new(Custodian, "savings");

    [Fact]
    public void ByKeyCommand_for_a_create_bearing_choice_carries_the_key_as_its_record()
    {
        var command = Account.CreditByKeyCommand(Key, new Account.Credit(7));

        command.TemplateId.Should().Be(Account.TemplateId);
        command.Choice.Should().Be(new ChoiceName("Credit"));
        command.ContractKey.Should().Be(Key.ToRecord());
        command.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Subject.GetRequiredField("delta").Should().Be(new DamlInt64(7));
    }

    [Fact]
    public void ByKeyCommand_for_a_value_returning_choice_carries_the_key_as_its_record()
    {
        var command = Account.CurrentBalanceByKeyCommand(Key, new Account.CurrentBalance());

        command.TemplateId.Should().Be(Account.TemplateId);
        command.Choice.Should().Be(new ChoiceName("CurrentBalance"));
        command.ContractKey.Should().Be(Key.ToRecord());
    }

    [Fact]
    public void ByKeyCommand_for_archive_encodes_an_empty_record_not_unit()
    {
        var command = Account.ArchiveByKeyCommand(Key);

        command.Choice.Should().Be(new ChoiceName("Archive"));
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
        command.ChoiceArgument.Should().NotBeOfType<DamlUnit>();
    }

    [Fact]
    public void ByKeyCommand_for_a_bare_party_key_carries_the_key_as_a_party_value()
    {
        var command = Steward.ReviseByKeyCommand(Custodian, new Steward.Revise("revised"));

        command.TemplateId.Should().Be(Steward.TemplateId);
        command.ContractKey.Should().Be(Custodian.ToDamlValue());
    }

    [Fact]
    public void ByKeyCommand_rejects_a_null_reference_typed_key()
    {
        var exercise = () => Account.CreditByKeyCommand(null!, new Account.Credit(7));

        exercise.Should().Throw<ArgumentNullException>().WithParameterName("key");
    }

    [Fact]
    public void ByKeyCommand_names_the_package_of_the_template()
    {
        var command = Account.CreditByKeyCommand(Key, new Account.Credit(7));

        command.PackageName.Should().Be("contractkeys-hcc6e8a4f16d9");
    }
}
