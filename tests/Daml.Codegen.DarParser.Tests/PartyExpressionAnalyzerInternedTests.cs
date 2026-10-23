// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

public sealed class PartyExpressionAnalyzerInternedTests
{
    private static Type PartyType =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } };

    private static Expr RecProjOnParam(int thisStr, int fieldStr) =>
        new() { RecProj = new Expr.Types.RecProj { FieldInternedStr = fieldStr, Record = new Expr { VarInternedStr = thisStr } } };

    private static Expr ConsWithHeads(Expr tail, params Expr[] heads)
    {
        var cons = new Expr.Types.Cons { Type = PartyType, Tail = tail };
        cons.Front.AddRange(heads);
        return new Expr { Cons = cons };
    }

    private static Expr Nil() => new() { Nil = new Expr.Types.Nil { Type = PartyType } };

    [Fact]
    public void Analyze_returns_dynamic_when_cons_tail_is_missing()
    {
        var thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("owner");
        var ctx = new PackageContext(package);

        var cons = new Expr.Types.Cons { Type = PartyType };
        cons.Front.Add(RecProjOnParam(thisStr, 1));
        var expr = new Expr { Cons = cons };

        PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_resolves_list_referenced_through_an_interned_expression()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("operator");
        package.InternedExprs.Add(ConsWithHeads(Nil(), RecProjOnParam(thisStr, 1)));
        var ctx = new PackageContext(package);

        var expr = new Expr { InternedExpr = 0 };

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("operator");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_interned_list_index_is_out_of_range()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        var ctx = new PackageContext(package);

        var expr = new Expr { InternedExpr = 99 };

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_resolves_party_head_referenced_through_an_interned_expression()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("custodian");
        package.InternedExprs.Add(RecProjOnParam(thisStr, 1));
        var ctx = new PackageContext(package);

        var headViaInterned = new Expr { InternedExpr = 0 };
        var expr = ConsWithHeads(Nil(), headViaInterned);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("custodian");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_head_interned_expr_index_is_out_of_range()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        var ctx = new PackageContext(package);

        var expr = ConsWithHeads(Nil(), new Expr { InternedExpr = 99 });

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_resolves_rec_proj_whose_record_is_an_interned_template_param()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("provider");
        package.InternedExprs.Add(new Expr { VarInternedStr = thisStr });
        var ctx = new PackageContext(package);

        var recProj = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = 1,
                Record = new Expr { InternedExpr = 0 },
            }
        };
        var expr = ConsWithHeads(Nil(), recProj);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("provider");
    }

    [Fact]
    public void Analyze_resolves_rec_proj_whose_record_is_reached_through_a_chain_of_interned_expressions()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("provider");
        package.InternedExprs.Add(new Expr { InternedExpr = 1 });
        package.InternedExprs.Add(new Expr { VarInternedStr = thisStr });
        var ctx = new PackageContext(package);

        var recProj = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = 1,
                Record = new Expr { InternedExpr = 0 },
            }
        };
        var expr = ConsWithHeads(Nil(), recProj);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "an interned index may point at another interned index, so a dereference that steps only once "
            + "lands on a node that matches no expected shape and demotes a resolvable field to Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("provider");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_rec_proj_record_interned_index_is_out_of_range()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("provider");
        var ctx = new PackageContext(package);

        var recProj = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = 1,
                Record = new Expr { InternedExpr = 99 },
            }
        };
        var expr = ConsWithHeads(Nil(), recProj);

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_rec_proj_field_interned_index_is_out_of_range()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        var ctx = new PackageContext(package);

        var expr = ConsWithHeads(Nil(), RecProjOnParam(thisStr, 99));

        PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "an out-of-range field index names no payload field, so there is no static party to record");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_value_indirection_field_interned_index_is_out_of_range()
    {
        const int thisStr = 0;
        const int dsStr = 1;
        const int importedPackageIdStr = 2;
        const int toPartiesModuleDname = 0;
        const int toPartiesNameDname = 1;
        const int signatoryModuleRef = 2;
        const int signatoryValueRef = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("some-imported-package-id");
        package.InternedStrings.Add("DA");
        package.InternedStrings.Add("Internal");
        package.InternedStrings.Add("Template");
        package.InternedStrings.Add("Functions");
        package.InternedStrings.Add("toParties");
        package.InternedDottedNames.Add(new InternedDottedName { SegmentsInternedStr = { 3, 4, 5, 6 } });
        package.InternedDottedNames.Add(new InternedDottedName { SegmentsInternedStr = { 7 } });

        var toParties = new Expr
        {
            Val = new ValueId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = importedPackageIdStr },
                    ModuleNameInternedDname = toPartiesModuleDname,
                },
                NameInternedDname = toPartiesNameDname,
            }
        };

        var signatoryBody = new Expr
        {
            Abs = new Expr.Types.Abs
            {
                Param = { new VarWithType { VarInternedStr = thisStr } },
                Body = new Expr
                {
                    Let = new Block
                    {
                        Bindings =
                        {
                            new Binding
                            {
                                Binder = new VarWithType { VarInternedStr = dsStr },
                                Bound = RecProjOnParam(thisStr, 99),
                            }
                        },
                        Body = new Expr
                        {
                            App = new Expr.Types.App
                            {
                                Fun = toParties,
                                Args = { new Expr { VarInternedStr = dsStr } },
                            }
                        },
                    }
                },
            }
        };

        package.Modules.Add(new Module
        {
            NameInternedDname = signatoryModuleRef,
            Values =
            {
                new DefValue
                {
                    NameWithType = new DefValue.Types.NameWithType { NameInternedDname = signatoryValueRef },
                    Expr = signatoryBody,
                }
            },
        });
        var ctx = new PackageContext(package);

        var topExpr = new Expr
        {
            App = new Expr.Types.App
            {
                Fun = new Expr
                {
                    Val = new ValueId
                    {
                        Module = new ModuleId
                        {
                            PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                            ModuleNameInternedDname = signatoryModuleRef,
                        },
                        NameInternedDname = signatoryValueRef,
                    }
                },
                Args = { new Expr { VarInternedStr = thisStr } },
            }
        };

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a let-bound projection reaches the party list through the single-Party wrapper, so an "
                + "out-of-range index there must bail exactly as the direct projection path does");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_rec_proj_has_no_record()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("owner");
        var ctx = new PackageContext(package);

        var recProj = new Expr { RecProj = new Expr.Types.RecProj { FieldInternedStr = 1 } };
        var expr = ConsWithHeads(Nil(), recProj);

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_rec_proj_record_is_not_a_var()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("owner");
        var ctx = new PackageContext(package);

        var recProj = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = 1,
                Record = new Expr { Nil = new Expr.Types.Nil { Type = PartyType } },
            }
        };
        var expr = ConsWithHeads(Nil(), recProj);

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_self_referential_interned_expr()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedExprs.Add(new Expr { InternedExpr = 0 });
        var ctx = new PackageContext(package);

        var expr = new Expr { InternedExpr = 0 };

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_self_referential_interned_cons_head()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedExprs.Add(new Expr { InternedExpr = 0 });
        var ctx = new PackageContext(package);

        var expr = ConsWithHeads(Nil(), new Expr { InternedExpr = 0 });

        PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_mutually_referential_interned_expr_pair()
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedExprs.Add(new Expr { InternedExpr = 1 });
        package.InternedExprs.Add(new Expr { InternedExpr = 0 });
        var ctx = new PackageContext(package);

        var expr = new Expr { InternedExpr = 0 };

        PartyExpressionAnalyzer.Analyze(expr, templateParamStr: 0, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_looping_forever_on_a_cyclic_interned_application_head()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedExprs.Add(new Expr { InternedExpr = 1 });
        package.InternedExprs.Add(new Expr { InternedExpr = 0 });
        var ctx = new PackageContext(package);

        var app = new Expr.Types.App { Fun = new Expr { InternedExpr = 0 } };
        app.Args.Add(new Expr { VarInternedStr = thisStr });
        var expr = new Expr { App = app };

        PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "the application head is dereferenced by a loop rather than by recursion, so a cyclic chain "
                + "there hangs the process instead of overflowing its stack, and no test reaches that loop "
                + "through the cons-chain walk");
    }

    [Fact]
    public void Analyze_resolves_the_same_interned_expr_referenced_from_two_sibling_heads()
    {
        const int thisStr = 0;
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("custodian");
        package.InternedExprs.Add(RecProjOnParam(thisStr, 1));
        var ctx = new PackageContext(package);

        var sharedHead = new Expr { InternedExpr = 0 };
        var expr = ConsWithHeads(Nil(), sharedHead, sharedHead);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().HaveCount(2);
        result.Parties.Select(p => ((DamlPartyPayloadField)p).FieldName)
            .Should().Equal("custodian", "custodian");
    }

    private static PackageContext InternedExprChainTo(Expr terminator, int links)
    {
        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("owner");
        for (var i = 0; i < links; i++)
        {
            package.InternedExprs.Add(new Expr { InternedExpr = i + 1 });
        }
        package.InternedExprs.Add(terminator);
        return new PackageContext(package);
    }

    [Fact]
    public void Analyze_resolves_an_interned_expression_chain_within_the_depth_bound()
    {
        const int thisStr = 0;
        const int ownerStr = 1;
        var ctx = InternedExprChainTo(
            ConsWithHeads(Nil(), RecProjOnParam(thisStr, ownerStr)),
            PartyExpressionAnalyzer.MaxExpressionDepth / 2);

        var result = PartyExpressionAnalyzer.Analyze(new Expr { InternedExpr = 0 }, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a depth bound that rejected a chain shorter than itself would turn resolvable clauses Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_an_interned_expression_chain_past_the_depth_bound()
    {
        const int thisStr = 0;
        const int ownerStr = 1;
        var ctx = InternedExprChainTo(
            ConsWithHeads(Nil(), RecProjOnParam(thisStr, ownerStr)),
            PartyExpressionAnalyzer.MaxExpressionDepth * 4);

        PartyExpressionAnalyzer.Analyze(new Expr { InternedExpr = 0 }, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "every index on the chain is distinct so the cycle guard never trips, leaving depth the only "
                + "thing that stops the walk at all");
    }

    [Fact]
    public void Analyze_resolves_an_interned_cons_head_chain_within_the_depth_bound()
    {
        const int thisStr = 0;
        const int ownerStr = 1;
        var ctx = InternedExprChainTo(
            RecProjOnParam(thisStr, ownerStr),
            PartyExpressionAnalyzer.MaxExpressionDepth / 2);

        var result = PartyExpressionAnalyzer.Analyze(
            ConsWithHeads(Nil(), new Expr { InternedExpr = 0 }), thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a depth bound that rejected a chain shorter than itself would turn resolvable clauses Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_an_interned_cons_head_chain_past_the_depth_bound()
    {
        const int thisStr = 0;
        const int ownerStr = 1;
        var ctx = InternedExprChainTo(
            RecProjOnParam(thisStr, ownerStr),
            PartyExpressionAnalyzer.MaxExpressionDepth * 4);

        PartyExpressionAnalyzer.Analyze(ConsWithHeads(Nil(), new Expr { InternedExpr = 0 }), thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a chain reached through a Cons head recurses in the single-party walk rather than the list "
                + "walk, so only that walk's own bound can stop it");
    }

    [Theory]
    [InlineData(255, DamlPartySource.Static)]
    [InlineData(256, DamlPartySource.Dynamic)]
    public void Analyze_bounds_an_interned_expression_chain_at_the_literal_depth_limit(int links, DamlPartySource expected)
    {
        var ctx = InternedExprChainTo(Nil(), links);

        PartyExpressionAnalyzer.Analyze(new Expr { InternedExpr = 0 }, templateParamStr: 0, ctx).Source
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(254, DamlPartySource.Static)]
    [InlineData(255, DamlPartySource.Dynamic)]
    public void Analyze_bounds_an_interned_cons_head_chain_at_the_literal_depth_limit(int links, DamlPartySource expected)
    {
        const int thisStr = 0;
        const int ownerStr = 1;
        var ctx = InternedExprChainTo(RecProjOnParam(thisStr, ownerStr), links);

        PartyExpressionAnalyzer.Analyze(ConsWithHeads(Nil(), new Expr { InternedExpr = 0 }), thisStr, ctx).Source
            .Should().Be(expected);
    }
}
