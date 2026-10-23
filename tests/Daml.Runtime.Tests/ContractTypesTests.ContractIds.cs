// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class ContractTypesTests
{
    private const string TestPackageId = "test-package-id";
    private const string TestPackageName = "test-package-name";
    private const string TestModuleName = "Test.Module";
    private static readonly Version TestPackageV1 = new(1, 0, 0);

    private sealed record TestTemplate(Party Owner, long Amount) : ITemplate
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(TestTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlInt64(Amount)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlInt64>().Value);
    }

    private const string PlaceholderThrowMessage =
        "'TestInterfacePlaceholder' is the C# placeholder for the Daml interface "
        + "'Test.Module:TestInterfacePlaceholder' and carries no template metadata. "
        + "Exercise interface choices directly on ContractId<TestInterfacePlaceholder>; "
        + "there is no supported coercion to a concrete ContractId<TConcrete>.";

    private sealed record TestInterfacePlaceholder : ITemplate
    {
        public static Identifier TemplateId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static string PackageName =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static Version PackageVersion =>
            throw new InvalidOperationException(PlaceholderThrowMessage);
        public static DamlTypeDescriptor DamlTypeId =>
            throw new InvalidOperationException(PlaceholderThrowMessage);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static TestInterfacePlaceholder FromRecord(DamlRecord record) => new();
    }

    private interface ITestInterfaceMarker : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "ITestInterfaceMarker");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => TestPackageV1;
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "ITestInterfaceMarker"), DamlTypeKind.Interface, TestPackageName);
    }

    private sealed record MyHolding(Party Owner) : ITemplate, IImplements<ITestInterfaceMarker>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(MyHolding));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("owner", Owner.ToDamlValue()));
    }

    [Fact]
    public void ContractId_should_store_value()
    {
        var contractId = new ContractId<TestTemplate>("contract-123");

        contractId.Value.Should().Be("contract-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ContractId_construction_rejects_null_empty_or_whitespace(string? value)
    {
        var act = () => new ContractId<TestTemplate>(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ContractId_should_convert_to_string_explicitly()
    {
        var contractId = new ContractId<TestTemplate>("contract-123");

        var stringValue = (string)contractId;

        stringValue.Should().Be("contract-123");
    }

    [Fact]
    public void ContractId_should_convert_from_string_explicitly()
    {
        var contractId = (ContractId<TestTemplate>)"contract-456";

        contractId.Value.Should().Be("contract-456");
    }

    [Fact]
    public void ContractId_ToString_should_return_value()
    {
        var contractId = new ContractId<TestTemplate>("contract-789");

        var result = contractId.ToString();

        result.Should().Be("contract-789");
    }

    [Fact]
    public void ContractId_ToDamlValue_should_create_DamlContractId()
    {
        var contractId = new ContractId<TestTemplate>("contract-abc");

        var damlValue = contractId.ToDamlValue();

        damlValue.Value.Should().Be("contract-abc");
        damlValue.TemplateId.Should().Be(TestTemplate.TemplateId);
    }

    [Fact]
    public void ContractId_should_support_equality()
    {
        var id1 = new ContractId<TestTemplate>("contract-123");
        var id2 = new ContractId<TestTemplate>("contract-123");
        var id3 = new ContractId<TestTemplate>("contract-456");

        id1.Should().Be(id2);
        id1.Should().NotBe(id3);
    }

    [Fact]
    public void ContractId_of_different_template_types_are_not_equal()
    {
        var sameTemplate1 = new ContractId<TestTemplate>("contract-x");
        var sameTemplate2 = new ContractId<TestTemplate>("contract-x");
        var differentTemplate = new ContractId<MyHolding>("contract-x");

        sameTemplate1.Should().Be(sameTemplate2);
        ((ContractId)sameTemplate1).Should().NotBe((ContractId)differentTemplate);
        sameTemplate1.Value.Should().Be(differentTemplate.Value);
    }

    [Fact]
    public void InterfacePlaceholder_can_satisfy_ContractId_T_constraint()
    {
        var cid = new ContractId<TestInterfacePlaceholder>("placeholder-cid-123");

        cid.Value.Should().Be("placeholder-cid-123");
        cid.ToString().Should().Be("placeholder-cid-123");
    }

    [Fact]
    public void InterfacePlaceholder_TemplateId_access_throws_with_explanatory_message()
    {
        var act = () => TestInterfacePlaceholder.TemplateId;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*placeholder for the Daml interface*Exercise interface choices directly*");
    }

    [Fact]
    public void InterfacePlaceholder_PackageId_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageId;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_PackageName_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageName;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_PackageVersion_access_throws()
    {
        var act = () => TestInterfacePlaceholder.PackageVersion;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InterfacePlaceholder_ContractId_ToDamlValue_propagates_throw_through_T_TemplateId()
    {
        var cid = new ContractId<TestInterfacePlaceholder>("c-1");
        var act = () => cid.ToDamlValue();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Exercise interface choices directly*");
    }

    [Fact]
    public void InterfacePlaceholder_round_trips_empty_record_without_throwing()
    {
        var placeholder = new TestInterfacePlaceholder();
        var record = placeholder.ToRecord();

        record.Should().NotBeNull();
        record.Fields.Should().BeEmpty();

        var recovered = TestInterfacePlaceholder.FromRecord(record);
        recovered.Should().Be(placeholder);
    }

    [Fact]
    public void InterfacePlaceholder_DamlContractId_ToTyped_compiles_and_constructs()
    {
        var damlCid = new DamlContractId("placeholder-cid-456");

        var typed = damlCid.ToTyped<TestInterfacePlaceholder>();

        typed.Value.Should().Be("placeholder-cid-456");
    }

    [Fact]
    public void IDamlType_marker_is_implemented_by_ITemplate()
    {
        typeof(IDamlType).IsAssignableFrom(typeof(ITemplate)).Should().BeTrue();
        typeof(IDamlType).IsAssignableFrom(typeof(TestTemplate)).Should().BeTrue();
    }

    [Fact]
    public void IDamlType_marker_is_implemented_by_IDamlInterface()
    {
        typeof(IDamlType).IsAssignableFrom(typeof(IDamlInterface)).Should().BeTrue();
        typeof(IDamlType).IsAssignableFrom(typeof(ITestInterfaceMarker)).Should().BeTrue();
    }

    [Fact]
    public void ContractId_T_accepts_an_interface_marker_as_T()
    {
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-1");

        cid.Value.Should().Be("interface-cid-1");
    }

    [Fact]
    public void ContractId_ToDamlValue_uses_InterfaceId_for_interface_markers()
    {
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-2");

        var damlCid = cid.ToDamlValue();

        damlCid.Value.Should().Be("interface-cid-2");
        damlCid.TemplateId.Should().Be(new Identifier(TestPackageId, TestModuleName, "ITestInterfaceMarker"));
    }

    [Fact]
    public void ContractId_ToDamlValue_uses_TemplateId_for_concrete_templates()
    {
        var cid = new ContractId<TestTemplate>("template-cid-3");

        var damlCid = cid.ToDamlValue();

        damlCid.Value.Should().Be("template-cid-3");
        damlCid.TemplateId.Should().Be(TestTemplate.TemplateId);
    }

    [Fact]
    public void DamlContractId_ToTyped_compiles_for_interface_marker()
    {
        var damlCid = new DamlContractId("interface-cid-4");

        var typed = damlCid.ToTyped<ITestInterfaceMarker>();

        typed.Value.Should().Be("interface-cid-4");
    }

    [Fact]
    public void ToInterfaceContractId_preserves_value_and_re_types_to_interface_marker()
    {
        var concreteCid = new ContractId<MyHolding>("template-cid-coerce");

        var interfaceCid = concreteCid.ToInterfaceContractId<MyHolding, ITestInterfaceMarker>();

        interfaceCid.Value.Should().Be("template-cid-coerce");
        interfaceCid.Should().BeOfType<ContractId<ITestInterfaceMarker>>();
    }

    [Fact]
    public void ExerciseCommand_For_uses_the_interface_id_in_the_template_id_slot()
    {
        var cid = new ContractId<ITestInterfaceMarker>("interface-cid-exec");
        var arg = new TestTemplate(new Party("alice"), 42L);

        var cmd = ExerciseCommand.For(cid, new ChoiceName("Transfer"), arg.ToRecord());

        cmd.TemplateId.Should().Be(new Identifier(TestPackageId, TestModuleName, "ITestInterfaceMarker"));
        cmd.ContractId.Value.Should().Be("interface-cid-exec");
        cmd.Choice.Should().Be(new ChoiceName("Transfer"));
    }

    [Fact]
    public void DamlContractId_should_store_value_and_template_id()
    {
        var templateId = new Identifier("pkg", "Module", "Template");

        var contractId = new DamlContractId("contract-id", templateId);

        contractId.Value.Should().Be("contract-id");
        contractId.TemplateId.Should().Be(templateId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void DamlContractId_should_reject_null_empty_and_whitespace_value(string? value)
    {
        var construct = () => new DamlContractId(value!);

        construct.Should().Throw<ArgumentException>().WithParameterName("Value");
    }

    [Fact]
    public void DamlContractId_should_reject_blank_value_through_with_expression()
    {
        var contractId = new DamlContractId("contract-id");

        var rewrite = () => contractId with { Value = " " };

        rewrite.Should().Throw<ArgumentException>().WithParameterName("Value");
    }

    [Fact]
    public void DamlContractId_should_allow_null_template_id()
    {
        var contractId = new DamlContractId("contract-id");

        contractId.Value.Should().Be("contract-id");
        contractId.TemplateId.Should().BeNull();
    }

    [Fact]
    public void DamlContractId_ToTyped_should_create_typed_contract_id()
    {
        var damlContractId = new DamlContractId("contract-xyz");

        var typedId = damlContractId.ToTyped<TestTemplate>();

        typedId.Value.Should().Be("contract-xyz");
    }

    [Fact]
    public void DamlContractId_ToString_should_return_value()
    {
        var contractId = new DamlContractId("contract-abc", new Identifier("pkg", "M", "T"));

        var result = contractId.ToString();

        result.Should().Be("contract-abc");
    }
}
