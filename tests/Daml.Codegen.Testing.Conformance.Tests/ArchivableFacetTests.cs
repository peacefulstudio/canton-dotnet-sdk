// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// The implicit Archive choice is reachable through <c>IArchivable&lt;TSelf&gt;</c> by code
/// that is generic over the owner. These tests run one generic helper against a corpus template
/// and a corpus interface, so neither the helper nor the generated builders spell the choice
/// name.
/// </summary>
public class ArchivableFacetTests
{
    private static ExerciseCommand ArchiveCommandFor<T>(ContractId<T> contractId)
        where T : IDamlType, IArchivable<T> =>
        ExerciseCommand.For(contractId, T.ChoiceArchive, DamlUnit.Instance);

    [Fact]
    public void Generic_archive_for_a_template_addresses_the_template_with_an_empty_record()
    {
        var command = ArchiveCommandFor(new ContractId<Account>("account-cid"));

        command.Choice.Should().Be(new ChoiceName("Archive"));
        command.ContractId.Value.Should().Be("account-cid");
        command.TemplateId.Should().Be(Account.TemplateId);
        command.PackageName.Should().Be("contractkeys-h5e25b75e405b");
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Generic_archive_for_an_interface_addresses_the_interface_with_an_empty_record()
    {
        var command = ArchiveCommandFor(new ContractId<IStewardship>("steward-cid"));

        command.Choice.Should().Be(new ChoiceName("Archive"));
        command.ContractId.Value.Should().Be("steward-cid");
        command.TemplateId.Should().Be(IStewardship.InterfaceId);
        command.PackageName.Should().BeNull();
        command.ChoiceArgument.Should().BeOfType<DamlRecord>().Subject.Fields.Should().BeEmpty();
    }

    [Fact]
    public void Generated_archive_builders_equal_the_generic_archive_command()
    {
        new ContractId<Account>("account-cid").ArchiveCommand()
            .Should().Be(ArchiveCommandFor(new ContractId<Account>("account-cid")));
        new ContractId<IStewardship>("steward-cid").ArchiveCommand()
            .Should().Be(ArchiveCommandFor(new ContractId<IStewardship>("steward-cid")));
    }

    [Fact]
    public void ChoiceArchive_through_the_facet_is_the_generated_descriptor()
    {
        Account.ChoiceArchive.Name.Should().Be(new ChoiceName("Archive"));
        Account.ChoiceArchive.Consuming.Should().BeTrue();
        IStewardship.ChoiceArchive.Name.Should().Be(new ChoiceName("Archive"));
        IStewardship.ChoiceArchive.Consuming.Should().BeTrue();
    }
}
