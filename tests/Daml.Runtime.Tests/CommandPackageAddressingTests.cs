// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandPackageAddressingTests
{
    private sealed record Widget(Party Owner) : ITemplate, IDamlRecord<Widget>
    {
        public static Identifier TemplateId { get; } = new("widget-package-id", "Acme.Widget", "Widget");
        public static string PackageId => "widget-package-id";
        public static string PackageName => "widget-package";
        public static Version PackageVersion => new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));

        public static Widget FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()));

        public static DamlRecord __ReadDamlLfJson(
            System.Text.Json.JsonElement json, Serialization.DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private interface IGadget : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new("gadget-package-id", "Acme.Gadget", "IGadget");
        static string IDamlInterface.PackageId => "gadget-package-id";
        static string IDamlInterface.PackageName => "gadget-package";
        static Version IDamlInterface.PackageVersion => new(1, 0, 0);
        static DamlTypeDescriptor IDamlType.DamlTypeId =>
            new(new Identifier("gadget-package-id", "Acme.Gadget", "IGadget"), DamlTypeKind.Interface, "gadget-package");
    }

    private static readonly Party Alice = new("party::alice");

    private static CommandsSubmission Submission(params ICommand[] commands) =>
        new CommandsSubmission(commands).WithActAs(Alice);

    private static CreateCommand CreateWidget() => CreateCommand.For(new Widget(Alice));

    private static ExerciseCommand ExerciseWidget() =>
        ExerciseCommand.For(new ContractId<Widget>("00widget"), new ChoiceName("Archive"), DamlUnit.Instance);

    private static ExerciseByKeyCommand ExerciseWidgetByKey() =>
        new(Widget.TemplateId, new DamlParty("party::alice"), new ChoiceName("Archive"), DamlUnit.Instance)
        {
            PackageName = Widget.PackageName,
        };

    private static CreateAndExerciseCommand CreateAndExerciseWidget() =>
        CreateAndExerciseCommand.For(new Widget(Alice), new ChoiceName("Archive"), DamlUnit.Instance);

    [Theory]
    [InlineData("")]
    [InlineData("-no-package-metadata")]
    [InlineData("UnknownPackage")]
    public void A_command_naming_a_placeholder_package_stays_on_its_package_id(string placeholderName)
    {
        var command = new CreateCommand(Widget.TemplateId, DamlRecord.Create()) { PackageName = placeholderName };

        Submission(command).AddressedCommands.Should().Equal(command);
    }

    [Fact]
    public void CreateCommand_For_carries_the_package_name_of_the_template()
    {
        CreateWidget().PackageName.Should().Be("widget-package");
    }

    [Fact]
    public void CreateCommand_constructed_directly_names_no_package()
    {
        new CreateCommand(Widget.TemplateId, DamlRecord.Create()).PackageName.Should().BeNull();
    }

    [Fact]
    public void CreateAndExerciseCommand_For_carries_the_package_name_of_the_template()
    {
        CreateAndExerciseWidget().PackageName.Should().Be("widget-package");
    }

    [Fact]
    public void ExerciseCommand_For_an_interface_names_no_package_so_it_stays_pinned_to_the_interface_package_id()
    {
        var command = ExerciseCommand.For(new ContractId<IGadget>("00gadget"), new ChoiceName("Ping"), DamlUnit.Instance);

        command.PackageName.Should().BeNull();
    }

    [Fact]
    public void AddressedCommands_name_a_template_by_package_name_by_default()
    {
        var addressed = Submission(CreateWidget()).AddressedCommands;

        addressed.Should().ContainSingle().Which.Should().BeOfType<CreateCommand>()
            .Which.TemplateId.Should().Be(new Identifier("#widget-package", "Acme.Widget", "Widget"));
    }

    [Fact]
    public void AddressedCommands_name_the_template_of_an_exercise_by_package_name()
    {
        var addressed = Submission(ExerciseWidget()).AddressedCommands;

        addressed.Should().ContainSingle().Which.Should().BeOfType<ExerciseCommand>()
            .Which.TemplateId.Should().Be(new Identifier("#widget-package", "Acme.Widget", "Widget"));
    }

    [Fact]
    public void AddressedCommands_name_the_template_of_an_exercise_by_key_by_package_name()
    {
        var addressed = Submission(ExerciseWidgetByKey()).AddressedCommands;

        addressed.Should().ContainSingle().Which.Should().BeOfType<ExerciseByKeyCommand>()
            .Which.TemplateId.Should().Be(new Identifier("#widget-package", "Acme.Widget", "Widget"));
    }

    [Fact]
    public void AddressedCommands_name_the_template_of_a_create_and_exercise_by_package_name()
    {
        var addressed = Submission(CreateAndExerciseWidget()).AddressedCommands;

        addressed.Should().ContainSingle().Which.Should().BeOfType<CreateAndExerciseCommand>()
            .Which.TemplateId.Should().Be(new Identifier("#widget-package", "Acme.Widget", "Widget"));
    }

    [Fact]
    public void AddressedCommands_keep_the_package_id_of_a_command_that_names_no_package()
    {
        var command = new CreateCommand(Widget.TemplateId, DamlRecord.Create());

        Submission(command).AddressedCommands.Should().Equal(command);
    }

    [Fact]
    public void AddressedCommands_keep_an_interface_exercise_on_the_interface_package_id()
    {
        var command = ExerciseCommand.For(new ContractId<IGadget>("00gadget"), new ChoiceName("Ping"), DamlUnit.Instance);

        Submission(command).AddressedCommands.Should().Equal(command);
    }

    [Fact]
    public void AddressedCommands_keep_the_package_name_of_a_command_they_renamed()
    {
        var addressed = Submission(ExerciseWidget()).AddressedCommands;

        addressed.Should().ContainSingle().Which.Should().BeOfType<ExerciseCommand>()
            .Which.PackageName.Should().Be("widget-package");
    }
}
