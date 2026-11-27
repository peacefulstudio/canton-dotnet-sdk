// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.data.{ImmArray, Ref}
import com.digitalasset.daml.lf.language.{Ast, LanguageVersion}

import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

class PartyExpressionAnalyzerSpec extends AnyWordSpec with Matchers {

  private val TParty: Ast.Type = Ast.TBuiltin(Ast.BTParty)
  private val PartyList: Ast.Type = Ast.TApp(Ast.TBuiltin(Ast.BTList), TParty)
  private val ThisBinder: String = "this"
  private val SomeTycon: Ast.TypeConApp =
    Ast.TypeConApp(
      tycon = Ref.Identifier.assertFromString(
        "0000000000000000000000000000000000000000000000000000000000000001:Mod:Tpl"
      ),
      args = ImmArray.empty,
    )

  private val NoResolution: PartyExpressionAnalyzer.ValueResolver = _ => None

  private val SpecPackageId: Ref.PackageId =
    Ref.PackageId.assertFromString("party-analyzer-spec-pkg")

  private val SpecModule: Ref.ModuleName = Ref.ModuleName.assertFromString("Mod")

  private def recProjOnThis(field: String, binder: String = ThisBinder): Ast.Expr =
    Ast.ERecProj(
      tycon = SomeTycon,
      field = Ref.Name.assertFromString(field),
      record = Ast.EVar(Ref.Name.assertFromString(binder)),
    )

  private def consList(heads: Ast.Expr*): Ast.Expr =
    Ast.ECons(
      typ = PartyList,
      front = ImmArray(heads: _*),
      tail = Ast.ENil(PartyList),
    )

  private def varOf(name: String): Ast.Expr = Ast.EVar(Ref.Name.assertFromString(name))

  private def absOf(param: String, body: Ast.Expr): Ast.Expr =
    Ast.EAbs((Ref.Name.assertFromString(param), PartyList), body)

  private def valueRefIn(packageId: String, name: String): Ast.Expr =
    Ast.EVal(Ref.Identifier.assertFromString(s"$packageId:Mod:$name"))

  private def importedValueRef(packageId: String, module: String, name: String): Ast.Expr =
    Ast.EVal(Ref.Identifier.assertFromString(s"$packageId:$module:$name"))

  private val ToParties: Ast.Expr =
    importedValueRef("some-imported-package-id", "DA.Internal.Template.Functions", "toParties")

  private def letOf(binder: String, bound: Ast.Expr, body: Ast.Expr): Ast.Expr =
    Ast.ELet(
      Ast.Binding(Some(Ref.Name.assertFromString(binder)), PartyList, bound),
      body,
    )

  private def appOf(fun: Ast.Expr, arg: Ast.Expr): Ast.Expr = Ast.EApp(fun, arg)

  private val SomeLocation: Ref.Location =
    Ref.Location(SpecPackageId, SpecModule, "dictSignatory", (1, 1), (1, 2))

  private def located(expr: Ast.Expr): Ast.Expr = Ast.ELocation(SomeLocation, expr)

  private def app2Of(fun: Ast.Expr, arg0: Ast.Expr, arg1: Ast.Expr): Ast.Expr =
    Ast.EApp(Ast.EApp(fun, arg0), arg1)

  private def selfValueRef(name: String): Ast.Expr = valueRefIn(SpecPackageId, name)

  private def resolverOver(values: (String, Ast.Expr)*): PartyExpressionAnalyzer.ValueResolver =
    PartyAnalyses.samePackageValueResolver(SpecPackageId, packageWithValues(values: _*))

  private def packageWithValues(values: (String, Ast.Expr)*): Ast.Package =
    Ast.Package(
      modules = Map(
        SpecModule -> Ast.Module(
          name = SpecModule,
          definitions = values.map { case (name, expr) =>
            Ref.DottedName.assertFromString(name) -> Ast.DValue(TParty, expr)
          }.toMap,
          templates = Map.empty,
          exceptions = Map.empty,
          interfaces = Map.empty,
          featureFlags = Ast.FeatureFlags.default,
        )
      ),
      directDeps = Set.empty,
      languageVersion = LanguageVersion.defaultLfVersion,
      metadata = Ast.PackageMetadata(
        name = Ref.PackageName.assertFromString("party-analyzer-spec"),
        version = Ref.PackageVersion.assertFromString("0.0.0"),
        upgradedPackageId = None,
      ),
      imports = Ast.DeclaredImports(Set.empty),
    )

  private def choiceControllerWrappedBy(
      wrapperName: String
  ): (Ast.Expr, PartyExpressionAnalyzer.ValueResolver) = {
    val wrapper =
      importedValueRef("some-imported-package-id", "DA.Internal.Template.Functions", wrapperName)
    val resolver = resolverOver(
      "dictController" -> absOf(
        "this",
        absOf("arg", appOf(wrapper, recProjOnThis("owner", binder = "this"))),
      )
    )
    (app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg")), resolver)
  }

  "PartyExpressionAnalyzer.analyze" should {

    "resolve a single payload-field signatory to Static" in {
      val expr = consList(recProjOnThis("platform"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("platform"))
    }

    "resolve multi-payload-field signatory in declaration order" in {
      val expr = consList(
        recProjOnThis("platform"),
        recProjOnThis("initiator"),
        recProjOnThis("counterparty"),
      )
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("platform", "initiator", "counterparty"))
    }

    "return Static empty for a literal empty list" in {
      PartyExpressionAnalyzer.analyze(Ast.ENil(PartyList), ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(Nil)
    }

    "return Dynamic when projection root is not the template parameter" in {
      val expr = consList(recProjOnThis("owner", binder = "x"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic for a function application" in {
      val app = Ast.EApp(
        fun = Ast.EVar(Ref.Name.assertFromString("helper")),
        arg = Ast.EVar(Ref.Name.assertFromString(ThisBinder)),
      )
      PartyExpressionAnalyzer.analyze(app, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when one element of a list is dynamic" in {
      val good = recProjOnThis("platform")
      val bad = Ast.EApp(
        fun = Ast.EVar(Ref.Name.assertFromString("mysteryFn")),
        arg = Ast.EVar(Ref.Name.assertFromString(ThisBinder)),
      )
      val expr = consList(good, bad)
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic for projection through the choice-argument binder" in {
      val expr = consList(recProjOnThis("requester", binder = "arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic for a null expression" in {
      PartyExpressionAnalyzer.analyze(null, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the Cons tail is not a Nil or another Cons" in {
      val malformedTail = Ast.EVar(Ref.Name.assertFromString("xs"))
      val expr = Ast.ECons(
        typ = PartyList,
        front = ImmArray(recProjOnThis("platform")),
        tail = malformedTail,
      )
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }
  }

  "PartyExpressionAnalyzer.analyze through toParties value indirection" should {

    "resolve a signatory through a same-package value indirection" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf("p", consList(recProjOnThis("dso", binder = "p")))
      )
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("dso"))
    }

    "resolve through a type-applied callee" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf("p", consList(recProjOnThis("owner", binder = "p")))
      )
      val expr = Ast.EApp(Ast.ETyApp(selfValueRef("dictSignatory"), TParty), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "resolve an indirected empty list to Static empty" in {
      val resolver = resolverOver("dictObserver" -> absOf("p", Ast.ENil(PartyList)))
      val expr = Ast.EApp(selfValueRef("dictObserver"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(Nil)
    }

    "return Dynamic when the callee references a different package" in {
      val resolver = resolverOver("dictSignatory" -> absOf("p", Ast.ENil(PartyList)))
      val expr = Ast.EApp(valueRefIn("some-other-package-id", "dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the callee value does not exist in the package" in {
      val resolver = resolverOver("dictSignatory" -> absOf("p", Ast.ENil(PartyList)))
      val expr = Ast.EApp(selfValueRef("missing"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the application has more than one argument" in {
      val resolver = resolverOver("dictSignatory" -> absOf("p", Ast.ENil(PartyList)))
      val expr = Ast.EApp(
        Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder)),
        varOf(ThisBinder),
      )
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the application argument is not the bound template parameter" in {
      val resolver = resolverOver("dictSignatory" -> absOf("p", Ast.ENil(PartyList)))
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf("other"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the type-applied callee is not a value reference" in {
      val expr = Ast.EApp(Ast.ETyApp(varOf("helper"), TParty), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the resolved value body does not match a supported shape" in {
      val resolver = resolverOver("dictSignatory" -> absOf("p", varOf("p")))
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the resolved value is not a function" in {
      val resolver = resolverOver("dictSignatory" -> Ast.ENil(PartyList))
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the resolved value takes more than one parameter" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf("a", absOf("b", Ast.ENil(PartyList)))
      )
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic for a deeper chain of same-package value indirection" in {
      val resolver = resolverOver(
        "dictY" -> absOf("q", Ast.ENil(PartyList)),
        "dictX" -> absOf("p", Ast.EApp(selfValueRef("dictY"), varOf("p"))),
      )
      val expr = Ast.EApp(selfValueRef("dictX"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic instead of overflowing for a self-referential value" in {
      val resolver = resolverOver(
        "dictX" -> absOf("p", Ast.EApp(selfValueRef("dictX"), varOf("p")))
      )
      val expr = Ast.EApp(selfValueRef("dictX"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }
  }

  "PartyExpressionAnalyzer.analyze through the choice controller indirection" should {

    "resolve a bare controller field through the curried toParties indirection to Static" in {
      val resolver = resolverOver(
        "dictController" -> absOf(
          "this",
          letOf(
            "ds",
            recProjOnThis("owner", binder = "this"),
            absOf("arg", appOf(ToParties, varOf("ds"))),
          ),
        )
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "resolve a controller field projected directly inside toParties to Static" in {
      val resolver = resolverOver(
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(ToParties, recProjOnThis("owner", binder = "this"))),
        )
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "resolve a controller field through the synthesized toParties class selector to Static" in {
      val (expr, resolver) = choiceControllerWrappedBy("$$ctoParties")
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    Seq("$$ctoParties1", "$$ctoParties2").foreach { selectorName =>
      s"return Dynamic when the choice indirection wraps the differently typed $selectorName selector" in {
        val (expr, resolver) = choiceControllerWrappedBy(selectorName)
        PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
          PartyAnalysisResult.Dynamic
      }
    }

    "return Dynamic when the choice indirection wraps a non-toParties helper" in {
      val helper = importedValueRef("some-imported-package-id", "Some.Other.Module", "resolveDelegate")
      val resolver = resolverOver(
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(helper, recProjOnThis("owner", binder = "this"))),
        )
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the choice indirection wraps the choice argument" in {
      val resolver = resolverOver(
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(ToParties, varOf("arg"))),
        )
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "return Dynamic when the wrapper is a same-package value named toParties" in {
      val samePackageToParties =
        importedValueRef(SpecPackageId, "DA.Internal.Template.Functions", "toParties")
      val resolver = resolverOver(
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(samePackageToParties, recProjOnThis("owner", binder = "this"))),
        )
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "resolve the single-argument toParties indirection of a template signatory to Static" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf(
          "this",
          letOf("ds", recProjOnThis("issuer", binder = "this"), appOf(ToParties, varOf("ds"))),
        )
      )
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("issuer"))
    }

    "resolve a controller reached through a same-package value definition to Static" in {
      val resolver = resolverOver(
        "dictParties" -> absOf(
          "p",
          letOf("ds", recProjOnThis("owner", binder = "p"), appOf(ToParties, varOf("ds"))),
        ),
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(selfValueRef("dictParties"), varOf("this"))),
        ),
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "return Dynamic when the same-package value definition projects the choice argument" in {
      val resolver = resolverOver(
        "dictParties" -> absOf(
          "p",
          letOf("ds", recProjOnThis("owner", binder = "p"), appOf(ToParties, varOf("ds"))),
        ),
        "dictController" -> absOf(
          "this",
          absOf("arg", appOf(selfValueRef("dictParties"), recProjOnThis("requester", binder = "arg"))),
        ),
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "resolve a template signatory whose reduction carries source locations" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf(
          "this",
          located(
            letOf(
              "ds",
              recProjOnThis("dso", binder = "this"),
              located(appOf(ToParties, located(varOf("ds")))),
            )
          ),
        )
      )
      val expr = Ast.EApp(selfValueRef("dictSignatory"), varOf(ThisBinder))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("dso"))
    }

    "resolve a located controller delegating to a located template signatory" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf(
          "this",
          located(
            letOf(
              "ds",
              recProjOnThis("dso", binder = "this"),
              located(appOf(ToParties, located(varOf("ds")))),
            )
          ),
        ),
        "dictController" -> absOf(
          "this",
          absOf("arg", located(appOf(selfValueRef("dictSignatory"), located(varOf("this"))))),
        ),
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("dso"))
    }

    "return Dynamic when a located projection roots in the choice argument" in {
      val resolver = resolverOver(
        "dictSignatory" -> absOf(
          "p",
          located(letOf("ds", recProjOnThis("dso", binder = "p"), appOf(ToParties, varOf("ds")))),
        ),
        "dictController" -> absOf(
          "this",
          absOf("arg", located(appOf(selfValueRef("dictSignatory"), located(recProjOnThis("requester", binder = "arg"))))),
        ),
      )
      val expr = app2Of(selfValueRef("dictController"), varOf(ThisBinder), varOf("arg"))
      PartyExpressionAnalyzer.analyze(expr, ThisBinder, resolver, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }
  }

  private def nestedConsOf(fieldNames: Seq[String]): Ast.Expr =
    fieldNames.foldRight(Ast.ENil(PartyList): Ast.Expr) { (field, tail) =>
      Ast.ECons(PartyList, ImmArray(recProjOnThis(field)), tail)
    }

  private def chainValueName(index: Int): String = s"chain$index"

  private def valueUnfoldingChain(links: Int): PartyExpressionAnalyzer.ValueResolver = {
    val last = chainValueName(links) -> absOf("p", appOf(ToParties, recProjOnThis("owner", binder = "p")))
    val forwarding = (0 until links).map { index =>
      chainValueName(index) -> absOf("p", appOf(selfValueRef(chainValueName(index + 1)), varOf("p")))
    }
    resolverOver(forwarding :+ last: _*)
  }

  private val ValueUnfoldingChainEntry: Ast.Expr =
    appOf(selfValueRef(chainValueName(0)), varOf(ThisBinder))

  private def applicationSpineEntry(extraLinks: Int): Ast.Expr =
    (0 until extraLinks).foldLeft(ValueUnfoldingChainEntry)((spine, _) => appOf(spine, varOf(ThisBinder)))

  private def typeApplicationSpineEntry(links: Int): Ast.Expr =
    appOf(
      (0 until links).foldLeft(selfValueRef(chainValueName(0)))((callee, _) => Ast.ETyApp(callee, TParty)),
      varOf(ThisBinder),
    )

  "PartyExpressionAnalyzer.analyze depth bound" should {

    "resolve a nested Cons chain within the depth bound to Static" in {
      val fields = (0 until 128).map(index => s"f$index")
      PartyExpressionAnalyzer.analyze(nestedConsOf(fields), ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Static(fields.toList)
    }

    "return Dynamic for a nested Cons chain past the depth bound" in {
      val fields = (0 until 1024).map(index => s"f$index")
      PartyExpressionAnalyzer.analyze(nestedConsOf(fields), ThisBinder, NoResolution, SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "resolve a value unfolding chain within the depth bound to Static" in {
      PartyExpressionAnalyzer.analyze(ValueUnfoldingChainEntry, ThisBinder, valueUnfoldingChain(128), SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "return Dynamic instead of overflowing for a value unfolding chain past the depth bound" in {
      PartyExpressionAnalyzer.analyze(ValueUnfoldingChainEntry, ThisBinder, valueUnfoldingChain(1024), SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "resolve an application spine within the depth bound to Static" in {
      PartyExpressionAnalyzer.analyze(applicationSpineEntry(128), ThisBinder, valueUnfoldingChain(0), SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "return Dynamic for an application spine past the depth bound" in {
      PartyExpressionAnalyzer.analyze(applicationSpineEntry(1024), ThisBinder, valueUnfoldingChain(0), SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }

    "resolve a type-application spine within the depth bound to Static" in {
      PartyExpressionAnalyzer.analyze(typeApplicationSpineEntry(128), ThisBinder, valueUnfoldingChain(0), SpecPackageId) shouldBe
        PartyAnalysisResult.Static(List("owner"))
    }

    "return Dynamic for a type-application spine past the depth bound" in {
      PartyExpressionAnalyzer.analyze(typeApplicationSpineEntry(1024), ThisBinder, valueUnfoldingChain(0), SpecPackageId) shouldBe
        PartyAnalysisResult.Dynamic
    }
  }
}
