// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandTypesTests
{
    // Test template for Command testing
    private sealed record TestTemplate(Party Owner, long Amount) : ITemplate
    {
        public static Identifier TemplateId => new("test-package", "Test.Module", "TestTemplate");
        public static string PackageId => "test-package";
        public static string PackageName => "test-package-name";
        public static Version PackageVersion => new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlInt64(Amount)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlInt64>().Value);
    }

    private interface TestInterfaceMarker : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new("test-package", "Test.Module", "TestInterfaceMarker");
        static string IDamlInterface.PackageId => "test-package";
        static string IDamlInterface.PackageName => "test-package-name";
        static Version IDamlInterface.PackageVersion => new(1, 0, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier("test-package", "Test.Module", "TestInterfaceMarker"), DamlTypeKind.Interface, "test-package-name");
    }

    private sealed class TraversalCountingList<T>(T value) : IReadOnlyList<T>
    {
        public int Traversals { get; private set; }

        public int Count => 1;

        public T this[int index]
        {
            get
            {
                Traversals++;
                return value;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            Traversals++;
            return Enumerable.Repeat(value, 1).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void CreateCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var args = DamlRecord.Create(DamlField.Create("field", new DamlText("value")));

        // Act
        var command = new CreateCommand(templateId, args);

        // Assert
        command.CommandType.Should().Be("Create");
        command.TemplateId.Should().Be(templateId);
        command.CreateArguments.Should().Be(args);
    }

    [Fact]
    public void CreateCommand_For_should_create_command_from_template()
    {
        // Arrange
        var template = new TestTemplate(new Party("Alice"), 100);

        // Act
        var command = CreateCommand.For(template);

        // Assert
        command.CommandType.Should().Be("Create");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.CreateArguments.GetField("owner")!.As<DamlParty>().Value.Should().Be("Alice");
        command.CreateArguments.GetField("amount")!.As<DamlInt64>().Value.Should().Be(100);
    }

    [Fact]
    public void ExerciseCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var contractId = new ContractId<TestTemplate>("contract-id-123");
        var choice = new ChoiceName("Transfer");
        var arg = new DamlText("argument");

        // Act
        var command = new ExerciseCommand(templateId, contractId, choice, arg);

        // Assert
        command.CommandType.Should().Be("Exercise");
        command.TemplateId.Should().Be(templateId);
        command.ContractId.Value.Should().Be("contract-id-123");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void ExerciseCommand_should_carry_a_typed_choice_name_that_unwraps_to_its_wire_string()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var arg = new DamlText("argument");

        var command = new ExerciseCommand(
            templateId,
            new ContractId<TestTemplate>("contract-id-123"),
            new ChoiceName("Transfer"),
            arg);

        command.Choice.Value.Should().Be("Transfer");
    }

    [Fact]
    public void ExerciseCommand_For_should_create_command_from_contract_id()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-123");
        var choice = new ChoiceName("Archive");
        var arg = DamlUnit.Instance;

        // Act
        var command = ExerciseCommand.For(contractId, choice, arg);

        // Assert
        command.CommandType.Should().Be("Exercise");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.ContractId.Value.Should().Be("contract-123");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ExerciseCommand_For_rejects_a_null_contract_id()
    {
        var act = () => ExerciseCommand.For<TestTemplate>(null!, new ChoiceName("Archive"), DamlUnit.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExerciseCommand_ForInterface_rejects_a_null_contract_id()
    {
#pragma warning disable CS0618
        var act = () => ExerciseCommand.ForInterface<TestInterfaceMarker>(null!, new ChoiceName("Transfer"), DamlUnit.Instance);
#pragma warning restore CS0618

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExerciseCommand_ForInterface_is_marked_Obsolete()
    {
        var method = typeof(ExerciseCommand).GetMethod(nameof(ExerciseCommand.ForInterface));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<ObsoleteAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void ExerciseCommand_For_should_create_command_from_an_interface_marker_contract_id()
    {
        var contractId = new ContractId<TestInterfaceMarker>("contract-456");
        var choice = new ChoiceName("Transfer");
        var arg = DamlUnit.Instance;

        var command = ExerciseCommand.For(contractId, choice, arg);

        command.TemplateId.Should().Be(DamlTypeIdOf<TestInterfaceMarker>().Identifier);
        command.ContractId.Value.Should().Be("contract-456");
        command.Choice.Should().Be(choice);
    }

    private static DamlTypeDescriptor DamlTypeIdOf<T>() where T : IDamlType => T.DamlTypeId;

    [Fact]
    public void ExerciseByKeyCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var key = new DamlText("contract-key");
        var choice = new ChoiceName("Transfer");
        var arg = new DamlInt64(100);

        // Act
        var command = new ExerciseByKeyCommand(templateId, key, choice, arg);

        // Assert
        command.CommandType.Should().Be("ExerciseByKey");
        command.TemplateId.Should().Be(templateId);
        command.ContractKey.Should().Be(key);
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void CreateAndExerciseCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create(DamlField.Create("field", new DamlText("value")));
        var choice = new ChoiceName("Archive");
        var choiceArg = DamlUnit.Instance;

        // Act
        var command = new CreateAndExerciseCommand(templateId, createArgs, choice, choiceArg);

        // Assert
        command.CommandType.Should().Be("CreateAndExercise");
        command.TemplateId.Should().Be(templateId);
        command.CreateArguments.Should().Be(createArgs);
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(choiceArg);
    }

    [Fact]
    public void CreateAndExerciseCommand_For_should_create_command_from_template()
    {
        // Arrange
        var template = new TestTemplate(new Party("Bob"), 200);
        var choice = new ChoiceName("Split");
        var arg = new DamlInt64(50);

        // Act
        var command = CreateAndExerciseCommand.For(template, choice, arg);

        // Assert
        command.CommandType.Should().Be("CreateAndExercise");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.CreateArguments.GetField("owner")!.As<DamlParty>().Value.Should().Be("Bob");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void CommandsSubmission_Single_should_create_submission_with_one_command()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Single(command, new Party("Alice"));

        // Assert
        submission.Commands.Should().HaveCount(1);
        submission.Commands[0].Should().Be(command);
        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
    }

    [Fact]
    public void CommandsSubmission_Single_should_create_submission_with_null_actAs()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Single(command);

        // Assert
        submission.Commands.Should().HaveCount(1);
        submission.ActAs.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_Multiple_should_create_submission_with_commands()
    {
        // Arrange
        var command1 = new CreateCommand(
            new Identifier("pkg", "Module", "Template1"),
            DamlRecord.Create());
        var command2 = new CreateCommand(
            new Identifier("pkg", "Module", "Template2"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Multiple(command1, command2);

        // Assert
        submission.Commands.Should().HaveCount(2);
        submission.Commands[0].Should().Be(command1);
        submission.Commands[1].Should().Be(command2);
    }

    [Fact]
    public void CommandsSubmission_should_carry_typed_ids_that_unwrap_to_their_wire_strings()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        var submission = new CommandsSubmission(
            [command],
            new WorkflowId("wf-1"),
            new CommandId("cmd-1"));

        submission.WorkflowId!.Value.Value.Should().Be("wf-1");
        submission.CommandId!.Value.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void CommandsSubmission_should_leave_optional_ids_null_when_omitted()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        var submission = new CommandsSubmission([command]);

        submission.WorkflowId.Should().BeNull();
        submission.CommandId.Should().BeNull();
        submission.SynchronizerId.Should().BeNull();
        submission.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithWorkflowId_should_set_workflow_id()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithWorkflowId(new WorkflowId("workflow-123"));

        // Assert
        result.WorkflowId.Should().Be(new WorkflowId("workflow-123"));
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void CommandsSubmission_WithOptionalWorkflowId_should_leave_the_submission_unchanged(string? workflowId)
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithOptionalWorkflowId(workflowId);

        // Assert
        result.WorkflowId.Should().BeNull();
        result.Should().Be(submission);
    }

    [Fact]
    public void CommandsSubmission_WithOptionalWorkflowId_should_set_workflow_id_when_supplied()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithOptionalWorkflowId("workflow-123");

        // Assert
        result.WorkflowId.Should().Be(new WorkflowId("workflow-123"));
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithWorkflowId_should_still_store_an_explicit_blank_workflow_id()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithWorkflowId(new WorkflowId(" "));

        // Assert
        result.WorkflowId.Should().Be(new WorkflowId(" "));
    }

    [Fact]
    public void CommandsSubmission_WithCommandId_should_set_command_id()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithCommandId(new CommandId("cmd-456"));

        // Assert
        result.CommandId.Should().Be(new CommandId("cmd-456"));
    }

    [Fact]
    public void CommandsSubmission_WithSynchronizerId_should_set_synchronizer_id()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithSynchronizerId(new SynchronizerId("global_sync::abc"));

        result.SynchronizerId.Should().Be(new SynchronizerId("global_sync::abc"));
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithActAs_should_set_parties()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithActAs(new Party("Alice"), new Party("Bob"));

        // Assert
        result.ActAs.Should().BeEquivalentTo(new[] { new Party("Alice"), new Party("Bob") });
    }

    [Fact]
    public void CommandsSubmission_WithReadAs_should_set_parties()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithReadAs(new Party("Charlie"), new Party("Diana"));

        // Assert
        result.ReadAs.Should().BeEquivalentTo(new[] { new Party("Charlie"), new Party("Diana") });
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_should_set_disclosed_contracts()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        // Act
        var result = submission.WithDisclosedContracts(disclosedContract);

        // Assert
        result.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_with_no_args_should_leave_disclosed_contracts_null()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithDisclosedContracts();

        result.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_with_null_array_should_leave_disclosed_contracts_null()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithDisclosedContracts(null);

        result.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void DisclosedContract_equality_should_compare_blob_content_across_allocations()
    {
        var left = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var right = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DisclosedContract_equality_should_distinguish_different_blob_content()
    {
        var identifier = new Identifier("pkg", "Module", "Template");
        var left = new DisclosedContract(
            "contract-id-1", identifier, "created-event-blob"u8.ToArray());
        var right = new DisclosedContract(
            "contract-id-1", identifier, "other-event-blob"u8.ToArray());

        left.Should().NotBe(right);
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void DisclosedContract_should_not_observe_mutation_of_the_source_blob_array()
    {
        var blob = "created-event-blob"u8.ToArray();
        var disclosedContract = new DisclosedContract(
            "contract-id-1", new Identifier("pkg", "Module", "Template"), blob);
        var hashCodeBeforeMutation = disclosedContract.GetHashCode();

        blob[0] ^= 0xFF;

        disclosedContract.CreatedEventBlob.ToArray().Should().Equal("created-event-blob"u8.ToArray());
        disclosedContract.GetHashCode().Should().Be(hashCodeBeforeMutation);
    }

    [Fact]
    public void DisclosedContract_should_stay_findable_in_a_hash_set_after_the_source_blob_array_mutates()
    {
        var blob = "created-event-blob"u8.ToArray();
        var disclosedContract = new DisclosedContract(
            "contract-id-1", new Identifier("pkg", "Module", "Template"), blob);
        var disclosedContracts = new HashSet<DisclosedContract> { disclosedContract };

        blob[0] ^= 0xFF;

        disclosedContracts.Contains(disclosedContract).Should().BeTrue();
        disclosedContracts.Contains(new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray())).Should().BeTrue();
    }

    [Fact]
    public void DisclosedContract_with_expression_should_copy_the_replacement_blob_array()
    {
        var original = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var replacementBlob = "replacement-event-blob"u8.ToArray();

        var replaced = original with { CreatedEventBlob = replacementBlob };
        replacementBlob[0] ^= 0xFF;

        replaced.CreatedEventBlob.ToArray().Should().Equal("replacement-event-blob"u8.ToArray());
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_should_preserve_disclosed_contracts()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var submission = CommandsSubmission.Single(command)
            .WithDisclosedContracts(disclosedContract);

        // Act
        var result = submission.WithSubmitter(new Party("Alice"));

        // Assert
        result.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void CommandsSubmission_should_leave_min_ledger_time_null_when_omitted()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Single(command);

        // Assert
        submission.MinLedgerTime.Should().BeNull();
        CommandsSubmission.Multiple(command).MinLedgerTime.Should().BeNull();
        new CommandsSubmission([command]).MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithMinLedgerTime_should_set_an_absolute_bound()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var instant = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithMinLedgerTime(new MinLedgerTime.Absolute(instant));

        // Assert
        result.MinLedgerTime.Should().Be(new MinLedgerTime.Absolute(instant));
        result.MinLedgerTime.Should().BeOfType<MinLedgerTime.Absolute>()
            .Which.Value.Should().Be(instant);
        submission.MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithMinLedgerTime_should_set_a_relative_bound()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithMinLedgerTime(
            new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)));

        // Assert
        result.MinLedgerTime.Should().BeOfType<MinLedgerTime.Relative>()
            .Which.Value.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CommandsSubmission_WithMinLedgerTime_should_round_trip_both_arms()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var instant = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);
        MinLedgerTime[] bounds =
        [
            new MinLedgerTime.Absolute(instant),
            new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)),
        ];

        // Act
        var readBack = bounds
            .Select(bound => CommandsSubmission.Single(command).WithMinLedgerTime(bound))
            .Select(submission => submission.MinLedgerTime?.Match(
                absolute => $"abs:{absolute:O}",
                relative => $"rel:{relative}") ?? "none")
            .ToArray();

        // Assert
        readBack.Should().Equal("abs:2026-08-15T09:30:00.0000000+00:00", "rel:00:00:30");
    }

    [Fact]
    public void CommandsSubmission_WithMinLedgerTime_with_null_should_clear_the_bound()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command)
            .WithMinLedgerTime(new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)));

        // Act
        var result = submission.WithMinLedgerTime(null);

        // Assert
        result.MinLedgerTime.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithMinLedgerTime_should_preserve_the_other_fields()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var submission = CommandsSubmission.Single(command)
            .WithWorkflowId(new WorkflowId("workflow-1"))
            .WithCommandId(new CommandId("cmd-1"))
            .WithActAs(new Party("Alice"))
            .WithReadAs(new Party("Bob"))
            .WithSynchronizerId(new SynchronizerId("sync-1"))
            .WithDisclosedContracts(disclosedContract);

        // Act
        var result = submission.WithMinLedgerTime(
            new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)));

        // Assert
        result.Commands.Should().ContainSingle().Which.Should().Be(command);
        result.WorkflowId.Should().Be(new WorkflowId("workflow-1"));
        result.CommandId.Should().Be(new CommandId("cmd-1"));
        result.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
        result.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
        result.SynchronizerId.Should().Be(new SynchronizerId("sync-1"));
        result.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
        result.MinLedgerTime.Should().BeOfType<MinLedgerTime.Relative>()
            .Which.Value.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_should_preserve_min_ledger_time()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command)
            .WithMinLedgerTime(new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)));

        // Act
        var result = submission.WithSubmitter(new Party("Alice"));

        // Assert
        result.MinLedgerTime.Should().BeOfType<MinLedgerTime.Relative>()
            .Which.Value.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CommandsSubmission_should_chain_fluent_methods()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        // Act
        var submission = CommandsSubmission.Single(command)
            .WithWorkflowId(new WorkflowId("workflow-1"))
            .WithCommandId(new CommandId("cmd-1"))
            .WithActAs(new Party("Alice"))
            .WithReadAs(new Party("Bob"))
            .WithDisclosedContracts(disclosedContract);

        // Assert
        submission.WorkflowId.Should().Be(new WorkflowId("workflow-1"));
        submission.CommandId.Should().Be(new CommandId("cmd-1"));
        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
        submission.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
        submission.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void CommandsSubmission_should_not_observe_mutation_of_the_source_commands_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var commands = new ICommand[] { command };
        var submission = new CommandsSubmission(commands);

        commands[0] = new CreateCommand(
            new Identifier("pkg", "Module", "Replaced"),
            DamlRecord.Create());

        submission.Commands.Should().ContainSingle().Which.Should().Be(command);
    }

    [Fact]
    public void CommandsSubmission_should_not_observe_mutation_of_the_source_actAs_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var actAs = new[] { new Party("Alice") };
        var submission = new CommandsSubmission([command], ActAs: actAs);

        actAs[0] = new Party("Mallory");

        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
    }

    [Fact]
    public void CommandsSubmission_should_not_observe_mutation_of_the_source_readAs_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var readAs = new[] { new Party("Bob") };
        var submission = new CommandsSubmission([command], ReadAs: readAs);

        readAs[0] = new Party("Mallory");

        submission.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
    }

    [Fact]
    public void CommandsSubmission_should_not_observe_mutation_of_the_source_disclosed_contracts_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var disclosedContracts = new[] { disclosedContract };
        var submission = new CommandsSubmission([command], DisclosedContracts: disclosedContracts);

        disclosedContracts[0] = new DisclosedContract(
            "contract-id-2",
            new Identifier("pkg", "Module", "Template"),
            "replacement-event-blob"u8.ToArray());

        submission.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void CommandsSubmission_WithActAs_should_not_observe_mutation_of_the_source_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var actAs = new[] { new Party("Alice") };
        var submission = CommandsSubmission.Single(command).WithActAs(actAs);

        actAs[0] = new Party("Mallory");

        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
    }

    [Fact]
    public void CommandsSubmission_WithReadAs_should_not_observe_mutation_of_the_source_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var readAs = new[] { new Party("Bob") };
        var submission = CommandsSubmission.Single(command).WithReadAs(readAs);

        readAs[0] = new Party("Mallory");

        submission.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_should_not_observe_mutation_of_the_source_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var disclosedContracts = new[] { disclosedContract };
        var submission = CommandsSubmission.Single(command).WithDisclosedContracts(disclosedContracts);

        disclosedContracts[0] = new DisclosedContract(
            "contract-id-2",
            new Identifier("pkg", "Module", "Template"),
            "replacement-event-blob"u8.ToArray());

        submission.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void CommandsSubmission_should_copy_each_list_exactly_once_on_primary_construction()
    {
        var commands = new TraversalCountingList<ICommand>(new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create()));
        var actAs = new TraversalCountingList<Party>(new Party("Alice"));
        var readAs = new TraversalCountingList<Party>(new Party("Bob"));
        var disclosedContracts = new TraversalCountingList<DisclosedContract>(new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray()));

        var submission = new CommandsSubmission(
            commands,
            ActAs: actAs,
            ReadAs: readAs,
            DisclosedContracts: disclosedContracts);

        commands.Traversals.Should().Be(1);
        actAs.Traversals.Should().Be(1);
        readAs.Traversals.Should().Be(1);
        disclosedContracts.Traversals.Should().Be(1);
        submission.Commands.Should().ContainSingle();
    }

    [Fact]
    public void CommandsSubmission_should_copy_each_list_exactly_once_through_a_with_expression()
    {
        var submission = CommandsSubmission.Single(new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create()));
        var actAs = new TraversalCountingList<Party>(new Party("Alice"));
        var readAs = new TraversalCountingList<Party>(new Party("Bob"));

        var replaced = submission with { ActAs = actAs, ReadAs = readAs };

        actAs.Traversals.Should().Be(1);
        readAs.Traversals.Should().Be(1);
        replaced.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
        replaced.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
    }

    [Fact]
    public void CommandsSubmission_should_reject_a_null_command_list()
    {
        var construct = () => new CommandsSubmission(null!);

        construct.Should().Throw<ArgumentNullException>()
            .WithParameterName("Commands");
    }

    [Fact]
    public void CommandsSubmission_equality_should_compare_its_lists_element_by_element_across_allocations()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        var left = new CommandsSubmission(
            [command],
            ActAs: [new Party("Alice")],
            ReadAs: [new Party("Bob")],
            DisclosedContracts: [disclosedContract]);
        var right = new CommandsSubmission(
            [command],
            ActAs: [new Party("Alice")],
            ReadAs: [new Party("Bob")],
            DisclosedContracts: [disclosedContract]);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void CommandsSubmission_equality_should_survive_two_submissions_sharing_one_source_array()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var commands = new ICommand[] { command };
        var actAs = new[] { new Party("Alice") };

        var left = new CommandsSubmission(commands, ActAs: actAs);
        var right = new CommandsSubmission(commands, ActAs: actAs);

        left.Should().Be(right);
    }

    [Fact]
    public void CommandsSubmission_equality_should_distinguish_absent_parties_from_an_empty_party_list()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        var absent = new CommandsSubmission([command]);
        var empty = new CommandsSubmission([command], ActAs: []);

        absent.Should().NotBe(empty);
    }

    [Fact]
    public void CommandsSubmission_should_stay_findable_in_a_hash_set_after_the_source_party_array_mutates()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var actAs = new[] { new Party("Alice") };
        var submission = new CommandsSubmission([command], ActAs: actAs);
        var submissions = new HashSet<CommandsSubmission> { submission };

        actAs[0] = new Party("Mallory");

        submissions.Contains(submission).Should().BeTrue();
        submissions.Contains(new CommandsSubmission([command], ActAs: [new Party("Alice")]))
            .Should().BeTrue();
    }

    [Fact]
    public void Choice_should_store_metadata()
    {
        // Arrange & Act
        var choice = new Choice<TestTemplate, DamlUnit, DamlUnit>
        {
            Name = new ChoiceName("Archive"),
            Consuming = true,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = _ => DamlUnit.Instance,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        // Assert
        choice.Name.Should().Be(new ChoiceName("Archive"));
        choice.Name.Value.Should().Be("Archive");
        choice.Consuming.Should().BeTrue();
    }

    [Fact]
    public void Choice_ArgumentEncoder_should_encode_argument()
    {
        // Arrange
        var choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ArgumentDecoder = val => val.As<DamlInt64>(),
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        // Act
        var encoded = choice.ArgumentEncoder(new DamlInt64(42));

        // Assert
        encoded.As<DamlInt64>().Value.Should().Be(42);
    }

    [Fact]
    public void Choice_ArgumentDecoder_should_decode_argument()
    {
        // Arrange
        var choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ArgumentDecoder = val => val.As<DamlInt64>(),
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        // Act
        var decoded = choice.ArgumentDecoder(new DamlInt64(42));

        // Assert
        decoded.Value.Should().Be(42);
    }

    [Fact]
    public void Choice_ResultDecoder_should_decode_result()
    {
        // Arrange
        var choice = new Choice<TestTemplate, DamlUnit, long>
        {
            Name = new ChoiceName("GetCount"),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = val => val.As<DamlInt64>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        // Act
        var decoded = choice.ResultDecoder(new DamlInt64(123));

        // Assert
        decoded.Should().Be(123);
    }

    [Fact]
    public void Choice_implements_IChoice_exposing_argument_and_result_types()
    {
        IChoice choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ArgumentDecoder = val => val.As<DamlInt64>(),
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        choice.ArgumentType.Should().Be<DamlInt64>();
        choice.ResultType.Should().Be<string>();
    }

    [Fact]
    public void Choice_IChoice_EncodeArgument_delegates_to_the_typed_ArgumentEncoder()
    {
        IChoice choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ArgumentDecoder = val => val.As<DamlInt64>(),
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        var encoded = choice.EncodeArgument(new DamlInt64(42));

        encoded.As<DamlInt64>().Value.Should().Be(42);
    }

    [Fact]
    public void Choice_IChoice_DecodeArgument_delegates_to_the_typed_ArgumentDecoder()
    {
        IChoice choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ArgumentDecoder = val => val.As<DamlInt64>(),
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        var decoded = choice.DecodeArgument(new DamlInt64(42));

        decoded.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Choice_IChoice_DecodeResult_delegates_to_the_typed_ResultDecoder()
    {
        IChoice choice = new Choice<TestTemplate, DamlUnit, long>
        {
            Name = new ChoiceName("GetCount"),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ArgumentDecoder = _ => DamlUnit.Instance,
            ResultDecoder = val => val.As<DamlInt64>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        var decoded = choice.DecodeResult(new DamlInt64(123));

        decoded.Should().BeOfType<long>().Which.Should().Be(123);
    }

    [Theory]
    [InlineData(nameof(IChoice.EncodeArgument))]
    [InlineData(nameof(IChoice.DecodeArgument))]
    [InlineData(nameof(IChoice.DecodeResult))]
    [InlineData(nameof(IChoice.DecodeArgumentJson))]
    [InlineData(nameof(IChoice.DecodeResultJson))]
    public void IChoice_wrapped_value_member_is_annotated_nullable(string memberName)
    {
        var method = typeof(IChoice).GetMethod(memberName)!;
        var nullability = new NullabilityInfoContext();

        var state = memberName == nameof(IChoice.EncodeArgument)
            ? nullability.Create(method.GetParameters()[0]).WriteState
            : nullability.Create(method.ReturnParameter).ReadState;

        state.Should().Be(NullabilityState.Nullable);
    }

    [Fact]
    public void Choice_IChoice_EncodeArgument_and_DecodeArgument_round_trip_a_null_argument()
    {
        IChoice choice = new Choice<TestTemplate, string?, string>
        {
            Name = new ChoiceName("Annotate"),
            Consuming = false,
            ArgumentEncoder = arg => arg is null ? DamlOptional.None : DamlOptional.Some(new DamlText(arg)),
            ArgumentDecoder = val => val.As<DamlOptional>().GetValueOrDefault<DamlText>()?.Value,
            ResultDecoder = val => val.As<DamlText>().Value,
            ArgumentJsonReader = (_, _) => throw new NotImplementedException(),
            ResultJsonReader = (_, _) => throw new NotImplementedException()
        };

        var encoded = choice.EncodeArgument(null);
        var decoded = choice.DecodeArgument(encoded);

        decoded.Should().BeNull();
    }
}
