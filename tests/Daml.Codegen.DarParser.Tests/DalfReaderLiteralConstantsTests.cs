// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class DalfReaderLiteralConstantsTests
{
    private static Expr Int64Literal(long value) => new() { BuiltinLit = new BuiltinLit { Int64 = value } };

    private static Expr TextLiteral(DalfPackageBuilder builder, string value) =>
        new() { BuiltinLit = new BuiltinLit { TextInternedStr = builder.Intern(value) } };

    private static Expr NumericLiteral(DalfPackageBuilder builder, string value) =>
        new() { BuiltinLit = new BuiltinLit { NumericInternedStr = builder.Intern(value) } };

    private static Expr BoolLiteral(bool value) =>
        new() { BuiltinCon = value ? BuiltinCon.ConTrue : BuiltinCon.ConFalse };

    private static Type IntType() => new() { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Int64 } };

    private static DefValue ValueNamed(DalfPackageBuilder builder, string[] nameSegments, Expr body) =>
        new()
        {
            NameWithType = new DefValue.Types.NameWithType
            {
                NameInternedDname = builder.DottedName(nameSegments),
                Type = IntType(),
            },
            Expr = body,
        };

    private static IReadOnlyList<DamlConstant> ConstantsOf(DalfPackageBuilder builder, params DefValue[] values)
    {
        var module = new Module { NameInternedDname = builder.DottedName("M") };
        module.Values.AddRange(values);
        builder.Package.Modules.Add(module);
        return DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package)).Modules.Single().Constants;
    }

    private static IReadOnlyList<DamlConstant> ConstantsOfNamed(DalfPackageBuilder builder, string name, Expr body) =>
        ConstantsOf(builder, ValueNamed(builder, [name], body));

    [Fact]
    public void Parse_reads_each_literal_kind_of_a_top_level_value_as_a_constant()
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOf(
            builder,
            ValueNamed(builder, ["greeting"], TextLiteral(builder, "hello")),
            ValueNamed(builder, ["retryLimit"], Int64Literal(42)),
            ValueNamed(builder, ["negativeLimit"], Int64Literal(-7)),
            ValueNamed(builder, ["maintenanceMode"], BoolLiteral(true)),
            ValueNamed(builder, ["disabled"], BoolLiteral(false)),
            ValueNamed(builder, ["feeRate"], NumericLiteral(builder, "0.0025000000")));

        constants.Should().Equal(
            new DamlConstant("disabled", new DamlBoolConstantValue(false)),
            new DamlConstant("feeRate", new DamlNumericConstantValue("0.0025000000")),
            new DamlConstant("greeting", new DamlTextConstantValue("hello")),
            new DamlConstant("maintenanceMode", new DamlBoolConstantValue(true)),
            new DamlConstant("negativeLimit", new DamlInt64ConstantValue(-7)),
            new DamlConstant("retryLimit", new DamlInt64ConstantValue(42)));
    }

    [Fact]
    public void Parse_orders_constants_by_ordinal_name()
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOf(
            builder,
            ValueNamed(builder, ["beta"], Int64Literal(1)),
            ValueNamed(builder, ["Zeta"], Int64Literal(2)),
            ValueNamed(builder, ["alpha"], Int64Literal(3)));

        constants.Select(constant => constant.Name).Should().Equal("Zeta", "alpha", "beta");
    }

    [Fact]
    public void Parse_follows_an_interned_expression_to_the_literal_it_names()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedExprs.Add(Int64Literal(99));
        var interned = new Expr { InternedExpr = 0 };

        var constants = ConstantsOfNamed(builder, "ninetyNine", interned);

        constants.Should().Equal(new DamlConstant("ninetyNine", new DamlInt64ConstantValue(99)));
    }

    [Fact]
    public void Parse_ignores_an_interned_expression_that_points_back_at_itself()
    {
        var builder = new DalfPackageBuilder();
        builder.Package.InternedExprs.Add(new Expr { InternedExpr = 0 });

        var constants = ConstantsOfNamed(builder, "looping", new Expr { InternedExpr = 0 });

        constants.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_an_interned_expression_index_outside_the_table()
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOfNamed(builder, "dangling", new Expr { InternedExpr = 5 });

        constants.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_a_unit_constructor_a_date_and_a_timestamp()
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOf(
            builder,
            ValueNamed(builder, ["unitValue"], new Expr { BuiltinCon = BuiltinCon.ConUnit }),
            ValueNamed(builder, ["someDate"], new Expr { BuiltinLit = new BuiltinLit { Date = 20000 } }),
            ValueNamed(builder, ["someInstant"], new Expr { BuiltinLit = new BuiltinLit { Timestamp = 1 } }));

        constants.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_a_generic_value_whose_literal_sits_under_a_type_abstraction()
    {
        var builder = new DalfPackageBuilder();
        var generic = new Expr
        {
            TyAbs = new Expr.Types.TyAbs { Body = Int64Literal(1) },
        };

        var constants = ConstantsOfNamed(builder, "phantom", generic);

        constants.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ignores_a_value_defined_by_a_computation_or_a_reference()
    {
        var builder = new DalfPackageBuilder();
        var application = new Expr { App = new Expr.Types.App { Fun = new Expr { Builtin = BuiltinFunction.AddInt64 } } };
        application.App.Args.Add(Int64Literal(6));
        application.App.Args.Add(Int64Literal(7));
        var reference = new Expr
        {
            Val = new ValueId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                    ModuleNameInternedDname = builder.DottedName("M"),
                },
                NameInternedDname = builder.DottedName("other"),
            },
        };

        var constants = ConstantsOf(
            builder,
            ValueNamed(builder, ["computed"], application),
            ValueNamed(builder, ["alias"], reference));

        constants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("$cshow")]
    [InlineData("$dmfoo")]
    [InlineData("$$cshow")]
    [InlineData("$$maxtag_AmqXsC4FTpt9WzsgSw953R")]
    [InlineData("cost$$")]
    [InlineData("$")]
    [InlineData("name$u00")]
    [InlineData("name$u00zz")]
    [InlineData("name$U0000004")]
    [InlineData("name$x")]
    public void Parse_ignores_a_value_the_compiler_named(string name)
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOfNamed(builder, name, Int64Literal(1));

        constants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("name$u0027")]
    [InlineData("name$U0001f389")]
    [InlineData("_hidden")]
    public void Parse_keeps_a_value_whose_user_spelling_the_archive_mangled(string name)
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOfNamed(builder, name, Int64Literal(1));

        constants.Should().Equal(new DamlConstant(name, new DamlInt64ConstantValue(1)));
    }

    [Fact]
    public void Parse_ignores_a_value_whose_name_has_more_than_one_segment()
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOf(builder, ValueNamed(builder, ["Outer", "inner"], Int64Literal(1)));

        constants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("+1.5", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("5.", "5")]
    [InlineData("5", "5")]
    public void Parse_normalises_a_numeric_literal_to_plain_decimal_notation(string archived, string expected)
    {
        var builder = new DalfPackageBuilder();

        var constants = ConstantsOfNamed(builder, "rate", NumericLiteral(builder, archived));

        constants.Should().Equal(new DamlConstant("rate", new DamlNumericConstantValue(expected)));
    }

    [Theory]
    [InlineData("1e5")]
    [InlineData("")]
    [InlineData("1.5m")]
    public void Parse_rejects_a_numeric_literal_outside_decimal_notation(string archived)
    {
        var builder = new DalfPackageBuilder();
        builder.Package.Modules.Add(new Module { NameInternedDname = builder.DottedName("M") });
        builder.Package.Modules[0].Values.Add(ValueNamed(builder, ["rate"], NumericLiteral(builder, archived)));

        var act = () => DalfReader.Parse(DalfTestFixtures.SerializeArchive(builder.Package));

        act.Should().Throw<InvalidDataException>().WithMessage("*rate*numeric*");
    }
}
