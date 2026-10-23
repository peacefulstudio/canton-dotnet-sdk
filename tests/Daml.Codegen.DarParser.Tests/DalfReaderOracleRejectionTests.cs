// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderOracleRejectionTests
{
    private static Type Builtin(BuiltinType builtin) =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = builtin } };

    private static Type StructWithOneTextField(DalfPackageBuilder builder)
    {
        var structType = new Type.Types.Struct();
        structType.Fields.Add(new FieldWithType { FieldInternedStr = builder.Intern("f"), Type = Builtin(BuiltinType.Text) });
        return new Type { Struct = structType };
    }

    private static Expr EmptyPartyList() =>
        new() { Nil = new Expr.Types.Nil { Type = Builtin(BuiltinType.Party) } };

    private static void ParseRecordWithFieldType(Func<DalfPackageBuilder, Type> fieldType, bool serializable = true)
    {
        var builder = new DalfPackageBuilder();
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("owner"), Type = fieldType(builder) });
        AddModuleWithDataType(
            builder,
            new DefDataType { NameInternedDname = builder.DottedName("R"), Record = record, Serializable = serializable });
        Parse(builder);
    }

    private static void AddModuleWithDataType(DalfPackageBuilder builder, DefDataType dataType)
    {
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.DataTypes.Add(dataType);
        builder.Package.Modules.Add(module);
    }

    private static DefTemplate TemplateWithValidClauses(DalfPackageBuilder builder)
    {
        var template = new DefTemplate
        {
            TyconInternedDname = builder.DottedName("T"),
            ParamInternedStr = builder.Intern("this"),
            Signatories = EmptyPartyList(),
            Observers = EmptyPartyList(),
        };
        return template;
    }

    private static TemplateChoice ChoiceWithValidClauses(DalfPackageBuilder builder) =>
        new()
        {
            NameInternedStr = builder.Intern("Go"),
            Controllers = EmptyPartyList(),
            Observers = EmptyPartyList(),
        };

    private static void ParseTemplate(DalfPackageBuilder builder, DefTemplate template)
    {
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Templates.Add(template);
        builder.Package.Modules.Add(module);
        Parse(builder);
    }

    private static void ParseInterfaceChoice(DalfPackageBuilder builder, TemplateChoice choice)
    {
        var iface = new DefInterface
        {
            TyconInternedDname = builder.DottedName("I"),
            ParamInternedStr = builder.Intern("this"),
        };
        iface.Choices.Add(choice);
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Interfaces.Add(iface);
        builder.Package.Modules.Add(module);
        Parse(builder);
    }

    private static void Parse(DalfPackageBuilder builder) =>
        DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

    [Fact]
    public void Parse_rejects_a_record_field_typed_as_a_struct()
    {
        var act = () => ParseRecordWithFieldType(StructWithOneTextField);

        act.Should().Throw<InvalidDataException>().WithMessage("*struct*not supported*");
    }

    [Fact]
    public void Parse_rejects_a_record_field_naming_a_struct_in_the_interned_type_pool()
    {
        var act = () => ParseRecordWithFieldType(builder =>
        {
            builder.Package.InternedTypes.Add(StructWithOneTextField(builder));
            return new Type { InternedType = 0 };
        });

        act.Should().Throw<InvalidDataException>().WithMessage("*struct*not supported*");
    }

    [Fact]
    public void Parse_accepts_a_struct_in_the_interned_type_pool_that_no_field_references()
    {
        var act = () => ParseRecordWithFieldType(builder =>
        {
            builder.Package.InternedTypes.Add(StructWithOneTextField(builder));
            return Builtin(BuiltinType.Text);
        });

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_accepts_a_non_serializable_record_whose_field_names_a_struct_in_the_interned_type_pool()
    {
        var act = () => ParseRecordWithFieldType(
            builder =>
            {
                builder.Package.InternedTypes.Add(StructWithOneTextField(builder));
                return new Type { InternedType = 0 };
            },
            serializable: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_accepts_a_non_serializable_record_whose_field_names_a_pool_entry_nesting_a_struct()
    {
        var act = () => ParseRecordWithFieldType(
            builder =>
            {
                var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
                list.Args.Add(StructWithOneTextField(builder));
                builder.Package.InternedTypes.Add(new Type { Builtin = list });
                return new Type { InternedType = 0 };
            },
            serializable: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_accepts_a_non_serializable_variant_whose_constructor_names_a_struct_in_the_interned_type_pool()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedTypes.Add(StructWithOneTextField(builder));
        var variant = new DefDataType.Types.Fields();
        variant.Fields_.Add(new FieldWithType { FieldInternedStr = builder.Intern("VSyn"), Type = new Type { InternedType = 0 } });
        AddModuleWithDataType(
            builder,
            new DefDataType { NameInternedDname = builder.DottedName("V"), Variant = variant, Serializable = false });

        var act = () => Parse(builder);

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_rejects_a_struct_nested_in_a_pool_entry_a_field_references()
    {
        var act = () => ParseRecordWithFieldType(builder =>
        {
            var list = new Type.Types.Builtin { Builtin_ = BuiltinType.List };
            list.Args.Add(StructWithOneTextField(builder));
            builder.Package.InternedTypes.Add(new Type { Builtin = list });
            return new Type { InternedType = 0 };
        });

        act.Should().Throw<InvalidDataException>().WithMessage("*struct*not supported*");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Parse_rejects_a_record_field_name_outside_the_interned_string_table(int index)
    {
        var builder = new DalfPackageBuilder();
        var record = new DefDataType.Types.Fields();
        record.Fields_.Add(new FieldWithType { FieldInternedStr = index, Type = Builtin(BuiltinType.Text) });
        AddModuleWithDataType(builder, new DefDataType { NameInternedDname = builder.DottedName("R"), Record = record, Serializable = true });

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage($"*interned-string index {index} is out of range*");
    }

    [Fact]
    public void Parse_rejects_an_enum_constructor_outside_the_interned_string_table()
    {
        var builder = new DalfPackageBuilder();
        var enumeration = new DefDataType.Types.EnumConstructors();
        enumeration.ConstructorsInternedStr.Add(99);
        AddModuleWithDataType(builder, new DefDataType { NameInternedDname = builder.DottedName("E"), Enum = enumeration, Serializable = true });

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned-string index 99 is out of range*");
    }

    [Fact]
    public void Parse_rejects_a_type_parameter_outside_the_interned_string_table()
    {
        var builder = new DalfPackageBuilder();
        var dataType = new DefDataType
        {
            NameInternedDname = builder.DottedName("R"),
            Record = new DefDataType.Types.Fields(),
            Serializable = true,
        };
        dataType.Params.Add(new TypeVarWithKind { VarInternedStr = 99 });
        AddModuleWithDataType(builder, dataType);

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned-string index 99 is out of range*");
    }

    [Fact]
    public void Parse_rejects_a_module_name_outside_the_interned_dotted_name_table()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.Modules.Add(new Module { NameInternedDname = 99 });

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned dotted-name index 99 is out of range*");
    }

    [Fact]
    public void Parse_rejects_a_dotted_name_segment_outside_the_interned_string_table()
    {
        var builder = new DalfPackageBuilder();
        var dottedName = new InternedDottedName();
        dottedName.SegmentsInternedStr.Add(99);
        builder.Package.InternedDottedNames.Add(dottedName);
        builder.Package.Modules.Add(new Module { NameInternedDname = builder.Package.InternedDottedNames.Count - 1 });

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned-string index 99 is out of range*");
    }

    [Fact]
    public void Parse_rejects_a_package_name_outside_the_interned_string_table()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.Metadata.NameInternedStr = 99;

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned-string index 99 is out of range*");
    }

    [Fact]
    public void Parse_rejects_a_package_version_outside_the_interned_string_table()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.Metadata.VersionInternedStr = 99;

        var act = () => Parse(builder);

        act.Should().Throw<InvalidDataException>().WithMessage("*interned-string index 99 is out of range*");
    }

    [Fact]
    public void Parse_accepts_a_template_whose_clauses_are_all_declared()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        template.Choices.Add(ChoiceWithValidClauses(builder));

        var act = () => ParseTemplate(builder, template);

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_rejects_a_template_with_cleared_signatories()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        template.Signatories = null;

        var act = () => ParseTemplate(builder, template);

        act.Should().Throw<InvalidDataException>().WithMessage("*signatory*declares no expression*");
    }

    [Fact]
    public void Parse_rejects_a_template_with_cleared_observers()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        template.Observers = null;

        var act = () => ParseTemplate(builder, template);

        act.Should().Throw<InvalidDataException>().WithMessage("*observer*declares no expression*");
    }

    [Fact]
    public void Parse_rejects_a_template_whose_observers_expression_has_no_case_set()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        template.Observers = new Expr();

        var act = () => ParseTemplate(builder, template);

        act.Should().Throw<InvalidDataException>().WithMessage("*observer*declares no expression*");
    }

    [Fact]
    public void Parse_rejects_a_template_choice_with_cleared_controllers()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        var choice = ChoiceWithValidClauses(builder);
        choice.Controllers = null;
        template.Choices.Add(choice);

        var act = () => ParseTemplate(builder, template);

        act.Should().Throw<InvalidDataException>().WithMessage("*controller*declares no expression*");
    }

    [Fact]
    public void Parse_rejects_a_template_choice_with_cleared_observers()
    {
        var builder = new DalfPackageBuilder();
        var template = TemplateWithValidClauses(builder);
        var choice = ChoiceWithValidClauses(builder);
        choice.Observers = null;
        template.Choices.Add(choice);

        var act = () => ParseTemplate(builder, template);

        act.Should().Throw<InvalidDataException>().WithMessage("*observer*declares no expression*");
    }

    [Fact]
    public void Parse_accepts_an_interface_choice_whose_clauses_are_declared()
    {
        var builder = new DalfPackageBuilder();

        var act = () => ParseInterfaceChoice(builder, ChoiceWithValidClauses(builder));

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_rejects_an_interface_choice_with_cleared_controllers()
    {
        var builder = new DalfPackageBuilder();
        var choice = ChoiceWithValidClauses(builder);
        choice.Controllers = null;

        var act = () => ParseInterfaceChoice(builder, choice);

        act.Should().Throw<InvalidDataException>().WithMessage("*controller*declares no expression*");
    }

    [Fact]
    public void Parse_rejects_an_interface_choice_with_cleared_observers()
    {
        var builder = new DalfPackageBuilder();
        var choice = ChoiceWithValidClauses(builder);
        choice.Observers = null;

        var act = () => ParseInterfaceChoice(builder, choice);

        act.Should().Throw<InvalidDataException>().WithMessage("*observer*declares no expression*");
    }
}
