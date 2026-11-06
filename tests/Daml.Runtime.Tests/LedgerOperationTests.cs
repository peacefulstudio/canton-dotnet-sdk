// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Runtime.Tests;

public class LedgerOperationTests
{
    private static readonly Identifier ConnectionRequest = new("pkg-1", "Connections.ConnectionRequest", "ConnectionRequest");
    private static readonly Identifier Connection = new("pkg-1", "Connections.Connection", "Connection");
    private static readonly Identifier InviteLink = new("pkg-1", "Connections.InviteLink", "InviteLink");

    [Fact]
    public void Label_joins_the_template_entity_name_and_the_operation_name()
    {
        var operation = new LedgerOperation.Command(ConnectionRequest, "Accept");

        operation.Label.Should().Be("ConnectionRequest.Accept");
    }

    [Fact]
    public void ToString_is_the_label()
    {
        new LedgerOperation.Command(ConnectionRequest, "Accept").ToString().Should().Be("ConnectionRequest.Accept");
    }

    [Fact]
    public void Batch_label_counts_each_operation_in_the_order_it_first_appears()
    {
        var batch = new LedgerOperation.Batch(
        [
            new LedgerOperation.Command(Connection, "Disconnect"),
            new LedgerOperation.Command(InviteLink, "Withdraw"),
            new LedgerOperation.Command(Connection, "Disconnect"),
            new LedgerOperation.Command(InviteLink, "Withdraw"),
            new LedgerOperation.Command(InviteLink, "Withdraw"),
        ]);

        batch.Label.Should().Be("Connection.Disconnect ×2, InviteLink.Withdraw ×3");
    }

    [Fact]
    public void Batch_label_names_an_operation_that_appears_once_without_a_count()
    {
        var batch = new LedgerOperation.Batch(
        [
            new LedgerOperation.Command(InviteLink, "Create"),
            new LedgerOperation.Command(Connection, "Disconnect"),
            new LedgerOperation.Command(Connection, "Disconnect"),
        ]);

        batch.Label.Should().Be("InviteLink.Create, Connection.Disconnect ×2");
        batch.ToString().Should().Be("InviteLink.Create, Connection.Disconnect ×2");
    }

    [Fact]
    public void Batch_rejects_an_empty_command_list()
    {
        var act = () => new LedgerOperation.Batch([]);

        act.Should().Throw<ArgumentException>().WithParameterName("Commands");
    }

    [Fact]
    public void Batch_equals_another_over_the_same_commands_in_the_same_order()
    {
        var first = new LedgerOperation.Batch([new(Connection, "Disconnect"), new(InviteLink, "Withdraw")]);
        var same = new LedgerOperation.Batch([new(Connection, "Disconnect"), new(InviteLink, "Withdraw")]);
        var reordered = new LedgerOperation.Batch([new(InviteLink, "Withdraw"), new(Connection, "Disconnect")]);

        first.Should().Be(same);
        first.GetHashCode().Should().Be(same.GetHashCode());
        first.Should().NotBe(reordered);
    }

    [Fact]
    public void Command_writes_its_template_and_name_with_no_discriminator_and_reads_back()
    {
        LedgerOperation operation = new LedgerOperation.Command(ConnectionRequest, "Accept");

        var written = JsonSerializer.Serialize(operation);

        written.Should().Be(
            """{"TemplateId":{"PackageId":"pkg-1","ModuleName":"Connections.ConnectionRequest","EntityName":"ConnectionRequest","FullyQualifiedName":"Connections.ConnectionRequest:ConnectionRequest"},"Name":"Accept"}""");
        JsonSerializer.Deserialize<LedgerOperation>(written).Should().Be(operation);
    }

    [Fact]
    public void Batch_writes_its_commands_and_reads_back()
    {
        LedgerOperation operation = new LedgerOperation.Batch(
            [new(Connection, "Disconnect"), new(InviteLink, "Withdraw")]);

        var written = JsonSerializer.Serialize(operation);

        written.Should().Be(
            """{"Commands":[{"TemplateId":{"PackageId":"pkg-1","ModuleName":"Connections.Connection","EntityName":"Connection","FullyQualifiedName":"Connections.Connection:Connection"},"Name":"Disconnect"},"""
            + """{"TemplateId":{"PackageId":"pkg-1","ModuleName":"Connections.InviteLink","EntityName":"InviteLink","FullyQualifiedName":"Connections.InviteLink:InviteLink"},"Name":"Withdraw"}]}""");
        JsonSerializer.Deserialize<LedgerOperation>(written).Should().Be(operation);
    }

    [Fact]
    public void Of_names_a_create_command_after_its_template_and_Create()
    {
        var command = new CreateCommand(ConnectionRequest, DamlRecord.Create());

        LedgerOperation.Of(command).Should().Be(new LedgerOperation.Command(ConnectionRequest, "Create"));
    }

    [Fact]
    public void Of_names_an_exercise_command_after_its_template_and_choice()
    {
        var command = new ExerciseCommand(
            ConnectionRequest, new ContractId<TestTemplate>("00aa"), new ChoiceName("Accept"), DamlRecord.Create());

        LedgerOperation.Of(command).Should().Be(new LedgerOperation.Command(ConnectionRequest, "Accept"));
    }

    [Fact]
    public void Of_names_an_exercise_by_key_command_after_its_template_and_choice()
    {
        var command = new ExerciseByKeyCommand(
            ConnectionRequest, new DamlText("key"), new ChoiceName("Expire"), DamlRecord.Create());

        LedgerOperation.Of(command).Should().Be(new LedgerOperation.Command(ConnectionRequest, "Expire"));
    }

    [Fact]
    public void Of_names_a_create_and_exercise_command_after_its_template_and_choice()
    {
        var command = new CreateAndExerciseCommand(
            ConnectionRequest, DamlRecord.Create(), new ChoiceName("Accept"), DamlRecord.Create());

        LedgerOperation.Of(command).Should().Be(new LedgerOperation.Command(ConnectionRequest, "Accept"));
    }
}
