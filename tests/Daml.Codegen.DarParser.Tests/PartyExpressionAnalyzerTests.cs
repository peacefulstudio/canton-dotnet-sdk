// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Unit tests for <see cref="PartyExpressionAnalyzer"/>. The analyzer walks a
/// Daml-LF expression rooted at a <c>List Party</c>-typed value (the shape used
/// for template <c>signatories</c> and choice <c>controllers</c>) and decides
/// whether it can be mapped to an ordered list of payload-field references.
///
/// <para>
/// We construct minimal LF expression trees by hand — building a full DAR for
/// every shape would be expensive and would entangle these tests with the
/// proto layer. The shapes we cover here are exactly the ones the analyzer is
/// meant to recognize (<c>Cons</c> chains of <c>RecProj</c> on the template
/// parameter), plus a handful of unsupported shapes that should drop to
/// <see cref="DamlPartySource.Dynamic"/>.
/// </para>
/// </summary>
public class PartyExpressionAnalyzerTests
{
    private static PackageContext MakeCtx(params string[] strings)
    {
        var package = new Package();
        foreach (var s in strings)
        {
            package.InternedStrings.Add(s);
        }
        return new PackageContext(package);
    }

    private static Expr RecProjOnTemplateParam(int thisStr, int fieldStr) =>
        new()
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = fieldStr,
                Record = new Expr { VarInternedStr = thisStr },
            }
        };

    /// <summary>
    /// Constructs <c>[head1, head2, ...] : List Party</c> as an LF
    /// <c>Cons</c> with a <c>Nil</c> tail. The element type doesn't matter
    /// for the analyzer — it only inspects the front-list shape.
    /// </summary>
    private static Expr ConsList(params Expr[] heads)
    {
        var cons = new Expr.Types.Cons
        {
            Type = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type
            {
                Builtin = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Builtin
                {
                    Builtin_ = BuiltinType.Party,
                }
            },
            Tail = new Expr
            {
                Nil = new Expr.Types.Nil
                {
                    Type = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type
                    {
                        Builtin = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Builtin
                        {
                            Builtin_ = BuiltinType.Party,
                        }
                    }
                }
            }
        };
        foreach (var head in heads)
        {
            cons.Front.Add(head);
        }
        return new Expr { Cons = cons };
    }

    [Fact]
    public void Analyze_resolves_single_payload_field_signatory()
    {
        const int thisStr = 0;
        const int platformStr = 1;
        var ctx = MakeCtx("this", "platform");

        var expr = ConsList(RecProjOnTemplateParam(thisStr, platformStr));

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().HaveCount(1);
        result.Parties[0].Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("platform");
    }

    [Fact]
    public void Analyze_resolves_multi_payload_field_signatory_in_declaration_order()
    {
        const int thisStr = 0;
        const int platformStr = 1;
        const int initiatorStr = 2;
        const int counterpartyStr = 3;
        var ctx = MakeCtx("this", "platform", "initiator", "counterparty");

        var expr = ConsList(
            RecProjOnTemplateParam(thisStr, platformStr),
            RecProjOnTemplateParam(thisStr, initiatorStr),
            RecProjOnTemplateParam(thisStr, counterpartyStr));

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().HaveCount(3);
        result.Parties.Select(p => ((DamlPartyPayloadField)p).FieldName)
            .Should().Equal("platform", "initiator", "counterparty");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_record_projection_is_not_on_template_param()
    {
        const int thisStr = 0;
        const int letBinderStr = 2;
        const int fieldStr = 1;
        var ctx = MakeCtx("this", "owner", "x");

        var projectionOnLetBinder = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = fieldStr,
                Record = new Expr { VarInternedStr = letBinderStr },
            }
        };
        var expr = ConsList(projectionOnLetBinder);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Dynamic,
            "the analyzer cannot tell statically what a let binder resolves to, so a projection off anything but the template parameter drops to Dynamic");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_function_application()
    {
        const int thisStr = 0;
        const int helperStr = 1;
        var ctx = MakeCtx("this", "helper");

        var app = new Expr
        {
            App = new Expr.Types.App
            {
                Fun = new Expr { VarInternedStr = helperStr },
                Args = { new Expr { VarInternedStr = thisStr } },
            }
        };

        var result = PartyExpressionAnalyzer.Analyze(app, thisStr, ctx);
        result.Source.Should().Be(DamlPartySource.Dynamic, "the analyzer cannot follow a function call statically");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_one_element_in_a_list_is_dynamic()
    {
        const int thisStr = 0;
        const int platformStr = 1;
        const int mysteryFnStr = 2;
        var ctx = MakeCtx("this", "platform", "mysteryFn");

        var payloadFieldHead = RecProjOnTemplateParam(thisStr, platformStr);
        var functionCallHead = new Expr
        {
            App = new Expr.Types.App
            {
                Fun = new Expr { VarInternedStr = mysteryFnStr },
                Args = { new Expr { VarInternedStr = thisStr } },
            }
        };
        var expr = ConsList(payloadFieldHead, functionCallHead);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);
        result.Source.Should().Be(
            DamlPartySource.Dynamic,
            "a partial signatory list would silently authorize a different contract, so one unresolvable element makes the whole list Dynamic");
    }

    [Fact]
    public void Analyze_resolves_observer_with_payload_field_reference_to_static()
    {
        const int thisStr = 0;
        const int holderStr = 1;
        var ctx = MakeCtx("this", "holder");

        var expr = ConsList(RecProjOnTemplateParam(thisStr, holderStr));

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "the analyzer is shape-only: observers, signatories and controllers share the List Party shape, so the same path resolves all three");
        result.Parties.Should().HaveCount(1);
        result.Parties[0].Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("holder");
    }

    [Fact]
    public void Analyze_returns_static_empty_for_literal_empty_observer_list()
    {
        const int thisStr = 0;
        var ctx = MakeCtx("this");

        var nil = new Expr
        {
            Nil = new Expr.Types.Nil
            {
                Type = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type
                {
                    Builtin = new Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type.Types.Builtin
                    {
                        Builtin_ = BuiltinType.Party,
                    }
                }
            }
        };

        var result = PartyExpressionAnalyzer.Analyze(nil, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a literal empty list is statically resolvable as no parties by design, distinct from Dynamic which means the analyzer could not tell");
        result.Parties.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_returns_dynamic_for_observer_referencing_choice_argument()
    {
        const int thisStr = 0;
        const int choiceArgBinderStr = 2;
        const int requesterStr = 1;
        var ctx = MakeCtx("this", "requester", "arg");

        var fromArg = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = requesterStr,
                Record = new Expr { VarInternedStr = choiceArgBinderStr },
            }
        };
        var expr = ConsList(fromArg);

        var result = PartyExpressionAnalyzer.Analyze(expr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Dynamic,
            "a projection rooted at the choice argument binder has no payload-field accessor, so the codegen falls back to the explicit-submitter shape");
    }
}
