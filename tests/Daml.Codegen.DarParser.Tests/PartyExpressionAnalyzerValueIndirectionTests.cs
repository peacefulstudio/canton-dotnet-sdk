// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;
using Type = Com.DigitalAsset.Daml.Lf.Archive.DamlLf2.Type;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Unit tests for the <see cref="PartyExpressionAnalyzer"/> function-application
/// indirection: a party-list expression whose top level applies a same-package
/// top-level value to the template's bound parameter — the shape this SDK's
/// generic-template desugaring emits for an ordinary <c>signatory dso</c>
/// clause, <c>App(Val(&lt;self&gt;, "$$csignatory"), [Var(this)])</c>, and for a
/// choice <c>controller &lt;field&gt;</c> clause, which passes the choice
/// argument alongside it.
/// </summary>
public sealed class PartyExpressionAnalyzerValueIndirectionTests
{
    private static Type PartyType =>
        new() { Builtin = new Type.Types.Builtin { Builtin_ = BuiltinType.Party } };

    private static Expr RecProjOnParam(int paramStr, int fieldStr) =>
        new() { RecProj = new Expr.Types.RecProj { FieldInternedStr = fieldStr, Record = new Expr { VarInternedStr = paramStr } } };

    private static Expr ConsWithHeads(Expr tail, params Expr[] heads)
    {
        var cons = new Expr.Types.Cons { Type = PartyType, Tail = tail };
        cons.Front.AddRange(heads);
        return new Expr { Cons = cons };
    }

    private static Expr Nil() => new() { Nil = new Expr.Types.Nil { Type = PartyType } };

    private static Expr Var(int str) => new() { VarInternedStr = str };

    private static Expr SelfValueRef(int moduleDname, int nameDname) => new()
    {
        Val = new ValueId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { SelfPackageId = new Unit() },
                ModuleNameInternedDname = moduleDname,
            },
            NameInternedDname = nameDname,
        }
    };

    private static Expr ImportedValueRef(int moduleDname, int nameDname, int importedPackageIdStr) => new()
    {
        Val = new ValueId
        {
            Module = new ModuleId
            {
                PackageId = new SelfOrImportedPackageId { ImportedPackageIdInternedStr = importedPackageIdStr },
                ModuleNameInternedDname = moduleDname,
            },
            NameInternedDname = nameDname,
        }
    };

    private static Expr PackageImportedValueRef(Package package, int moduleDname, int nameDname)
    {
        package.PackageImports = new PackageImports { ImportedPackages = { "some-imported-package-id" } };
        return new Expr
        {
            Val = new ValueId
            {
                Module = new ModuleId
                {
                    PackageId = new SelfOrImportedPackageId { PackageImportId = 0 },
                    ModuleNameInternedDname = moduleDname,
                },
                NameInternedDname = nameDname,
            }
        };
    }

    private static Expr AppOf(Expr fun, params Expr[] args)
    {
        var app = new Expr.Types.App { Fun = fun };
        app.Args.AddRange(args);
        return new Expr { App = app };
    }

    private static Expr TyAppOf(Expr expr) =>
        new() { TyApp = new Expr.Types.TyApp { Expr = expr, Types_ = { PartyType } } };

    private static Expr AbsOf(int paramStr, Expr body)
    {
        var abs = new Expr.Types.Abs { Body = body };
        abs.Param.Add(new VarWithType { VarInternedStr = paramStr });
        return new Expr { Abs = abs };
    }

    private static Expr CurriedAbsOf(Expr body, params int[] paramStrs)
    {
        var abs = new Expr.Types.Abs { Body = body };
        foreach (var p in paramStrs)
        {
            abs.Param.Add(new VarWithType { VarInternedStr = p });
        }
        return new Expr { Abs = abs };
    }

    private static Binding BindingOf(int binderStr, Expr bound) =>
        new() { Binder = new VarWithType { VarInternedStr = binderStr }, Bound = bound };

    private static Expr LetOf(Expr body, params Binding[] bindings)
    {
        var block = new Block { Body = body };
        block.Bindings.AddRange(bindings);
        return new Expr { Let = block };
    }

    private static int AddInternedString(Package package, string value)
    {
        var existing = package.InternedStrings.IndexOf(value);
        if (existing >= 0)
        {
            return existing;
        }
        package.InternedStrings.Add(value);
        return package.InternedStrings.Count - 1;
    }

    private static int AddDottedName(Package package, params string[] segments)
    {
        var dottedName = new InternedDottedName();
        foreach (var segment in segments)
        {
            dottedName.SegmentsInternedStr.Add(AddInternedString(package, segment));
        }
        package.InternedDottedNames.Add(dottedName);
        return package.InternedDottedNames.Count - 1;
    }

    private static void AddModuleValue(Package package, int moduleDname, int nameDname, Expr expr)
    {
        var module = package.Modules.FirstOrDefault(m => m.NameInternedDname == moduleDname);
        if (module is null)
        {
            module = new Module { NameInternedDname = moduleDname };
            package.Modules.Add(module);
        }
        module.Values.Add(new DefValue
        {
            NameWithType = new DefValue.Types.NameWithType { NameInternedDname = nameDname },
            Expr = expr,
        });
    }

    [Fact]
    public void Analyze_resolves_signatory_through_same_package_value_indirection()
    {
        const int thisStr = 0;
        const int innerParamStr = 1;
        const int dsoStr = 2;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        package.InternedStrings.Add("dso");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(innerParamStr, ConsWithHeads(Nil(), RecProjOnParam(innerParamStr, dsoStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "the callee's bound parameter is a different interned string than the template's this, so resolving proves the analyzer substitutes rather than relying on both binders sharing a name");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("dso");
    }

    [Fact]
    public void Analyze_resolves_through_a_type_applied_callee()
    {
        const int thisStr = 0;
        const int fieldStr = 1;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("owner");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(thisStr, ConsWithHeads(Nil(), RecProjOnParam(thisStr, fieldStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(TyAppOf(SelfValueRef(moduleDname, nameDname)), Var(thisStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "the dictionary method is generic over the template type, so the callee arrives wrapped in one type application");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_static_empty_list_through_value_indirection()
    {
        const int thisStr = 0;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(thisStr, Nil()));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "an observer clause with no explicit observers compiles to a callee whose body is a bare empty list, and an indirected-but-empty list is Static rather than Dynamic");
        result.Parties.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_returns_dynamic_when_callee_references_an_imported_package()
    {
        const int thisStr = 0;
        const int importedPackageIdStr = 1;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("some-other-package-id");
        var ctx = new PackageContext(package);

        var topExpr = AppOf(ImportedValueRef(moduleDname: 10, nameDname: 20, importedPackageIdStr), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a callee living in another package, such as the stdlib's toParties, is unresolvable because this context cannot see other packages' modules");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_callee_value_does_not_exist_in_the_package()
    {
        const int thisStr = 0;
        const int moduleDname = 10;

        var package = new Package();
        package.InternedStrings.Add("this");
        AddModuleValue(package, moduleDname, nameDname: 20, AbsOf(thisStr, Nil()));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname: 999), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a module without a matching DefValue is a malformed DALF or a never-defined name, which GetValueExpr must report as not found rather than throw");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_a_two_argument_application_resolves_to_a_literal_list()
    {
        const int thisStr = 0;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(thisStr, Nil()));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_application_argument_is_not_the_bound_template_parameter()
    {
        const int thisStr = 0;
        const int someOtherBinderStr = 1;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("other");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(thisStr, Nil()));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(someOtherBinderStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_type_applied_callee_is_not_a_value_reference()
    {
        const int thisStr = 0;
        const int helperStr = 1;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("helper");
        var ctx = new PackageContext(package);

        var topExpr = AppOf(TyAppOf(Var(helperStr)), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_resolved_value_body_does_not_match_a_supported_shape()
    {
        const int thisStr = 0;
        const int innerParamStr = 1;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(innerParamStr, Var(innerParamStr)));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_resolved_value_is_not_a_function()
    {
        const int thisStr = 0;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        AddModuleValue(package, moduleDname, nameDname, Nil());
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a malformed DALF applies a value whose defining expression is not a lambda, which a well-formed DAR would not type-check, so the analyzer must bail rather than throw or guess");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_resolved_value_takes_more_than_one_parameter()
    {
        const int thisStr = 0;
        const int paramAStr = 1;
        const int paramBStr = 2;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("a");
        package.InternedStrings.Add("b");
        AddModuleValue(package, moduleDname, nameDname, CurriedAbsOf(Nil(), paramAStr, paramBStr));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a curried function of arity 2 applied to 1 argument would need an invented expression for the still-unbound second parameter");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_a_deeper_chain_of_same_package_value_indirection()
    {
        const int thisStr = 0;
        const int xParamStr = 1;
        const int yParamStr = 2;
        const int moduleDname = 10;
        const int xNameDname = 20;
        const int yNameDname = 21;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        package.InternedStrings.Add("q");
        AddModuleValue(package, moduleDname, yNameDname, AbsOf(yParamStr, Nil()));
        AddModuleValue(package, moduleDname, xNameDname, AbsOf(xParamStr, AppOf(SelfValueRef(moduleDname, yNameDname), Var(xParamStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, xNameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "the list walk resolves one level of value indirection only, and a list terminator is not a "
                + "single-party field the reduce can unfold through");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_self_referential_value()
    {
        const int thisStr = 0;
        const int paramStr = 1;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        AddModuleValue(package, moduleDname, nameDname, AbsOf(paramStr, AppOf(SelfValueRef(moduleDname, nameDname), Var(paramStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a value whose body applies itself must terminate as Dynamic rather than recurse until an uncatchable StackOverflowException");
    }

    private static Expr ImportedToPartiesRef(Package package, int importedPackageIdStr)
    {
        var moduleDname = AddDottedName(package, "DA", "Internal", "Template", "Functions");
        var nameDname = AddDottedName(package, "toParties");
        return ImportedValueRef(moduleDname, nameDname, importedPackageIdStr);
    }

    [Fact]
    public void Analyze_resolves_choice_controller_field_through_curried_toParties_indirection()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int dsStr = 2;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            LetOf(
                AbsOf(argStr, AppOf(toParties, Var(dsStr))),
                BindingOf(dsStr, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a choice controller clause compiles to a two-argument application of the synthesized per-choice controller value to the template parameter and the choice argument, whose body lets ds be the payload field and wraps it in the imported single-Party list wrapper toParties");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_choice_controller_field_projected_directly_inside_to_parties()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "the field projection is the direct argument of toParties with no intervening let binding");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_controller_field_whose_projection_is_a_chain_of_interned_expressions()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");
        package.InternedExprs.Add(new Expr { InternedExpr = 1 });
        package.InternedExprs.Add(RecProjOnParam(thisStr, ownerStr));

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, new Expr { InternedExpr = 0 })));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "an interned index may point at another interned index, so dereferencing the wrapper argument "
            + "only once lands on a node that is neither a variable nor a projection");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_controller_field_projected_off_a_chain_of_interned_expressions()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");
        package.InternedExprs.Add(new Expr { InternedExpr = 1 });
        package.InternedExprs.Add(Var(thisStr));

        var projectionThroughInternedRecord = new Expr
        {
            RecProj = new Expr.Types.RecProj
            {
                FieldInternedStr = ownerStr,
                Record = new Expr { InternedExpr = 0 },
            }
        };
        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, projectionThroughInternedRecord)));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "the projected record reaches the template parameter through two interned hops, and a "
            + "single-step dereference stops on the intermediate index");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_choice_indirection_wraps_a_non_toParties_helper()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var helperModuleDname = AddDottedName(package, "Some", "Other", "Module");
        var helperNameDname = AddDottedName(package, "resolveDelegate");
        var helper = ImportedValueRef(helperModuleDname, helperNameDname, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(helper, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "an imported helper other than toParties could transform the party, for example return a delegate, so a bare field argument to it does not prove the controller is that payload field");
    }

    [Fact]
    public void Analyze_resolves_choice_controller_field_through_the_synthesized_to_parties_class_selector()
    {
        var ctx = ContextWithChoiceControllerWrapper("$$ctoParties", out var topExpr, out var thisStr);

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Theory]
    [InlineData("$$ctoParties1")]
    [InlineData("$$ctoParties2")]
    public void Analyze_returns_dynamic_for_the_differently_typed_numbered_to_parties_selectors(string selectorName)
    {
        var ctx = ContextWithChoiceControllerWrapper(selectorName, out var topExpr, out var thisStr);

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void Analyze_resolves_the_wrapper_imported_through_the_package_import_table()
    {
        var ctx = ContextWithChoiceControllerWrapper("$$ctoParties", out var topExpr, out var thisStr, viaPackageImportTable: true);

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_choice_controller_applied_one_argument_at_a_time()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr)), Var(argStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_choice_controller_reached_through_an_interned_expression()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        package.InternedExprs.Add(AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr)));
        var ctx = new PackageContext(package);

        var topExpr = new Expr { InternedExpr = 0 };

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_single_party_field_through_a_deeper_same_package_value_chain()
    {
        const int thisStr = 0;
        const int dsStr = 2;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int outerModuleDname = 2;
        const int outerNameDname = 3;
        const int innerNameDname = 4;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        AddModuleValue(package, outerModuleDname, innerNameDname, AbsOf(dsStr, AppOf(toParties, Var(dsStr))));
        AddModuleValue(package, outerModuleDname, outerNameDname,
            AbsOf(thisStr, AppOf(SelfValueRef(outerModuleDname, innerNameDname), RecProjOnParam(thisStr, ownerStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(outerModuleDname, outerNameDname), Var(thisStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_value_chain_that_unfolds_into_itself()
    {
        const int thisStr = 0;
        const int dsStr = 2;
        const int ownerStr = 3;
        const int outerModuleDname = 2;
        const int outerNameDname = 3;
        const int innerNameDname = 4;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");

        AddModuleValue(package, outerModuleDname, innerNameDname,
            AbsOf(dsStr, AppOf(SelfValueRef(outerModuleDname, outerNameDname), Var(dsStr))));
        AddModuleValue(package, outerModuleDname, outerNameDname,
            AbsOf(thisStr, AppOf(SelfValueRef(outerModuleDname, innerNameDname), RecProjOnParam(thisStr, ownerStr))));
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(outerModuleDname, outerNameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    private static PackageContext ContextWithChoiceControllerWrapper(
        string wrapperName,
        out Expr topExpr,
        out int thisStr,
        bool viaPackageImportTable = false)
    {
        thisStr = 0;
        const int argStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var wrapperModuleDname = AddDottedName(package, "DA", "Internal", "Template", "Functions");
        var wrapperNameDname = AddDottedName(package, wrapperName);
        var wrapper = viaPackageImportTable
            ? PackageImportedValueRef(package, wrapperModuleDname, wrapperNameDname)
            : ImportedValueRef(wrapperModuleDname, wrapperNameDname, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(wrapper, RecProjOnParam(thisStr, ownerStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);

        topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));
        return new PackageContext(package);
    }

    [Fact]
    public void Analyze_returns_dynamic_when_the_choice_indirection_wraps_the_choice_argument()
    {
        const int thisStr = 0;
        const int argStr = 1;
        const int importedPackageIdStr = 4;
        const int controllerModuleDname = 2;
        const int controllerNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var controllerBody = AbsOf(thisStr,
            AbsOf(argStr, AppOf(toParties, Var(argStr))));
        AddModuleValue(package, controllerModuleDname, controllerNameDname, controllerBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(controllerModuleDname, controllerNameDname), Var(thisStr), Var(argStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a controller derived from the choice argument rather than the payload resolves to the choice-arg binder, not a template field");
    }

    [Fact]
    public void Analyze_resolves_template_signatory_field_through_the_single_argument_toParties_indirection()
    {
        const int thisStr = 0;
        const int dsStr = 2;
        const int issuerStr = 3;
        const int importedPackageIdStr = 4;
        const int signatoryModuleDname = 2;
        const int signatoryNameDname = 3;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("arg");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("issuer");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        var signatoryBody = AbsOf(thisStr,
            LetOf(AppOf(toParties, Var(dsStr)), BindingOf(dsStr, RecProjOnParam(thisStr, issuerStr))));
        AddModuleValue(package, signatoryModuleDname, signatoryNameDname, signatoryBody);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(signatoryModuleDname, signatoryNameDname), Var(thisStr));

        var result = PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx);

        result.Source.Should().Be(DamlPartySource.Static);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("issuer");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_crashing_for_a_value_whose_abs_has_no_body()
    {
        const int thisStr = 0;
        const int paramStr = 1;
        const int moduleDname = 10;
        const int nameDname = 20;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        var bodylessAbs = new Expr { Abs = new Expr.Types.Abs() };
        bodylessAbs.Abs.Param.Add(new VarWithType { VarInternedStr = paramStr });
        AddModuleValue(package, moduleDname, nameDname, bodylessAbs);
        var ctx = new PackageContext(package);

        var topExpr = AppOf(SelfValueRef(moduleDname, nameDname), Var(thisStr));

        PartyExpressionAnalyzer.Analyze(topExpr, thisStr, ctx).Source
            .Should().Be(DamlPartySource.Dynamic);
    }

    private const int ChainModuleDname = 100;

    private static int ChainValueNameDname(int index) => 1000 + index;

    private static PackageContext ValueUnfoldingChain(int links) =>
        new(ValueUnfoldingChainPackage(links));

    private static Package ValueUnfoldingChainPackage(int links)
    {
        const int paramStr = 1;
        const int ownerStr = 3;
        const int importedPackageIdStr = 4;

        var package = new Package();
        package.InternedStrings.Add("this");
        package.InternedStrings.Add("p");
        package.InternedStrings.Add("ds");
        package.InternedStrings.Add("owner");
        package.InternedStrings.Add("some-imported-package-id");

        var toParties = ImportedToPartiesRef(package, importedPackageIdStr);
        AddModuleValue(
            package,
            ChainModuleDname,
            ChainValueNameDname(links),
            AbsOf(paramStr, AppOf(toParties, RecProjOnParam(paramStr, ownerStr))));
        for (var i = links - 1; i >= 0; i--)
        {
            AddModuleValue(
                package,
                ChainModuleDname,
                ChainValueNameDname(i),
                AbsOf(paramStr, AppOf(SelfValueRef(ChainModuleDname, ChainValueNameDname(i + 1)), Var(paramStr))));
        }

        return package;
    }

    private static Expr ValueUnfoldingChainEntry(int thisStr) =>
        AppOf(SelfValueRef(ChainModuleDname, ChainValueNameDname(0)), Var(thisStr));

    [Fact]
    public void Analyze_resolves_a_value_unfolding_chain_within_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(PartyExpressionAnalyzer.MaxExpressionDepth / 2);

        var result = PartyExpressionAnalyzer.Analyze(ValueUnfoldingChainEntry(thisStr), thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a depth bound that rejected a chain shorter than itself would turn resolvable clauses Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_instead_of_overflowing_for_a_value_unfolding_chain_past_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(PartyExpressionAnalyzer.MaxExpressionDepth * 4);

        PartyExpressionAnalyzer.Analyze(ValueUnfoldingChainEntry(thisStr), thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "every value on the chain is distinct so the unfolded-value guard never trips, leaving depth "
                + "the only thing that stops the walk at all");
    }

    private static Expr ApplicationChainEntry(int thisStr, int extraLinks)
    {
        var expr = ValueUnfoldingChainEntry(thisStr);
        for (var i = 0; i < extraLinks; i++)
        {
            expr = AppOf(expr, Var(thisStr));
        }
        return expr;
    }

    private static Expr TypeApplicationChainEntry(int thisStr, int links)
    {
        var callee = SelfValueRef(ChainModuleDname, ChainValueNameDname(0));
        for (var i = 0; i < links; i++)
        {
            callee = TyAppOf(callee);
        }
        return AppOf(callee, Var(thisStr));
    }

    [Fact]
    public void Analyze_resolves_an_application_spine_within_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        var result = PartyExpressionAnalyzer.Analyze(
            ApplicationChainEntry(thisStr, PartyExpressionAnalyzer.MaxExpressionDepth / 2), thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a link bound that rejected a spine shorter than itself would turn resolvable clauses Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_resolves_a_type_application_spine_within_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        var result = PartyExpressionAnalyzer.Analyze(
            TypeApplicationChainEntry(thisStr, PartyExpressionAnalyzer.MaxExpressionDepth / 2), thisStr, ctx);

        result.Source.Should().Be(
            DamlPartySource.Static,
            "a link bound that rejected a spine shorter than itself would turn resolvable clauses Dynamic");
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_an_application_spine_past_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        PartyExpressionAnalyzer.Analyze(
            ApplicationChainEntry(thisStr, PartyExpressionAnalyzer.MaxExpressionDepth * 4), thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "an application spine is flattened by a loop no other bound constrains, so only its own link "
                + "bound keeps a hand-crafted chain from being ground through");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_a_type_application_spine_past_the_depth_bound()
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        PartyExpressionAnalyzer.Analyze(
            TypeApplicationChainEntry(thisStr, PartyExpressionAnalyzer.MaxExpressionDepth * 4), thisStr, ctx).Source
            .Should().Be(
                DamlPartySource.Dynamic,
                "a type-application spine is unwrapped by a loop no other bound constrains, so only its own "
                + "link bound keeps a hand-crafted chain from being ground through");
    }

    private const string InternedSpineReason =
        "chaining each spine link through the interned-expression table nests no proto message, so this is the "
        + "shape that reaches the walk from a real archive — a directly nested spine this long dies on the "
        + "reader's own protobuf recursion limit first";

    private static Expr InternedApplicationSpine(Package package, int links, int thisStr)
    {
        for (var i = 0; i < links; i++)
        {
            package.InternedExprs.Add(AppOf(new Expr { InternedExpr = i + 1 }, Var(thisStr)));
        }
        package.InternedExprs.Add(SelfValueRef(ChainModuleDname, ChainValueNameDname(0)));
        return new Expr { InternedExpr = 0 };
    }

    private static Expr InternedTypeApplicationSpine(Package package, int links, int thisStr)
    {
        for (var i = 0; i < links; i++)
        {
            package.InternedExprs.Add(TyAppOf(new Expr { InternedExpr = i + 1 }));
        }
        package.InternedExprs.Add(SelfValueRef(ChainModuleDname, ChainValueNameDname(0)));
        return AppOf(new Expr { InternedExpr = 0 }, Var(thisStr));
    }

    [Fact]
    public void Analyze_resolves_an_interned_application_spine_within_the_depth_bound()
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedApplicationSpine(package, PartyExpressionAnalyzer.MaxExpressionDepth / 2, thisStr);

        var result = PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package));

        result.Source.Should().Be(DamlPartySource.Static, InternedSpineReason);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_an_interned_application_spine_past_the_depth_bound()
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedApplicationSpine(package, PartyExpressionAnalyzer.MaxExpressionDepth * 4, thisStr);

        PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package)).Source
            .Should().Be(DamlPartySource.Dynamic, InternedSpineReason);
    }

    [Fact]
    public void Analyze_resolves_an_interned_type_application_spine_within_the_depth_bound()
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedTypeApplicationSpine(package, PartyExpressionAnalyzer.MaxExpressionDepth / 2, thisStr);

        var result = PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package));

        result.Source.Should().Be(DamlPartySource.Static, InternedSpineReason);
        result.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
    }

    [Fact]
    public void Analyze_returns_dynamic_for_an_interned_type_application_spine_past_the_depth_bound()
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedTypeApplicationSpine(package, PartyExpressionAnalyzer.MaxExpressionDepth * 4, thisStr);

        PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package)).Source
            .Should().Be(DamlPartySource.Dynamic, InternedSpineReason);
    }

    [Theory]
    [InlineData(256, DamlPartySource.Static)]
    [InlineData(257, DamlPartySource.Dynamic)]
    public void Analyze_bounds_an_interned_application_spine_at_exactly_256_links(int links, DamlPartySource expected)
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedApplicationSpine(package, links, thisStr);

        PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package)).Source
            .Should().Be(expected, InternedSpineReason);
    }

    [Theory]
    [InlineData(256, DamlPartySource.Static)]
    [InlineData(257, DamlPartySource.Dynamic)]
    public void Analyze_bounds_an_interned_type_application_spine_at_exactly_256_links(int links, DamlPartySource expected)
    {
        const int thisStr = 0;
        var package = ValueUnfoldingChainPackage(0);
        var entry = InternedTypeApplicationSpine(package, links, thisStr);

        PartyExpressionAnalyzer.Analyze(entry, thisStr, new PackageContext(package)).Source
            .Should().Be(expected, InternedSpineReason);
    }

    [Theory]
    [InlineData(255, DamlPartySource.Static)]
    [InlineData(256, DamlPartySource.Dynamic)]
    public void Analyze_bounds_a_direct_application_spine_at_exactly_256_links(int extraLinks, DamlPartySource expected)
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        PartyExpressionAnalyzer.Analyze(ApplicationChainEntry(thisStr, extraLinks), thisStr, ctx).Source
            .Should().Be(expected, "the entry application is the first link and each extra link wraps it once");
    }

    [Theory]
    [InlineData(256, DamlPartySource.Static)]
    [InlineData(257, DamlPartySource.Dynamic)]
    public void Analyze_bounds_a_direct_type_application_spine_at_exactly_256_links(int links, DamlPartySource expected)
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(0);

        PartyExpressionAnalyzer.Analyze(TypeApplicationChainEntry(thisStr, links), thisStr, ctx).Source
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(254, DamlPartySource.Static)]
    [InlineData(255, DamlPartySource.Dynamic)]
    public void Analyze_bounds_a_value_unfolding_chain_at_the_literal_depth_limit(int links, DamlPartySource expected)
    {
        const int thisStr = 0;
        var ctx = ValueUnfoldingChain(links);

        PartyExpressionAnalyzer.Analyze(ValueUnfoldingChainEntry(thisStr), thisStr, ctx).Source
            .Should().Be(expected);
    }
}
