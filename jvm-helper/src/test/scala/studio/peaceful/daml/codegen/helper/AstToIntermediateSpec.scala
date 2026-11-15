// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.{
  BuiltinType => PbBuiltinType,
  IntermediatePackage,
}

import org.scalatest.{Inspectors, OptionValues}
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.nio.file.Paths

class AstToIntermediateSpec extends AnyWordSpec with Matchers with OptionValues with Inspectors {

  private val FixtureDar = Paths.get(
    sys.props.getOrElse("jvmHelper.testFixtureDar",
      SpliceSnapshotDars.path("splice-api-token-holding-v1").toString
    )
  )

  private lazy val translated = {
    val dar = SchemaDecoder.readDar(FixtureDar.toFile).getOrElse(
      fail(s"could not load fixture DAR at $FixtureDar")
    )
    AstToIntermediate.translate(dar)
  }

  private def moduleNamed(pkg: IntermediatePackage, segments: Seq[String]) =
    pkg.modules
      .find(_.nameSegments == segments)
      .getOrElse(fail(s"module ${segments.mkString(".")} not found in package ${pkg.packageName}"))

  "AstToIntermediate" should {

    "carry the main package id, name, version and language version" in {
      val main = translated.main.value
      main.packageId       should fullyMatch regex "[0-9a-f]{64}"
      main.packageName     shouldBe "splice-api-token-holding-v1"
      main.packageVersion  shouldBe "1.0.0"
      main.languageVersion should not be empty
    }

    "carry upgraded_package_id (empty when the package is not an upgrade)" in {
      val main = translated.main.value
      main.upgradedPackageId shouldBe ""
    }

    "list the daml-prim and daml-stdlib dependencies" in {
      val depNames = translated.dependencies.map(_.packageName).toSet
      depNames should contain allOf ("daml-prim", "daml-stdlib")
    }

    "emit an empty record placeholder alongside each Daml interface" in {
      val main = translated.main.value
      val mod  = moduleNamed(main, Seq("Splice", "Api", "Token", "HoldingV1"))
      val placeholder = mod.dataTypes
        .find(_.name == "Holding")
        .getOrElse(fail("expected an empty record placeholder named 'Holding' to back the Daml interface"))
      placeholder.shape.isRecord shouldBe true
      placeholder.getRecord.fields shouldBe empty
      placeholder.typeParameters shouldBe empty
      placeholder.isSerializable shouldBe false
    }

    "emit exactly one placeholder data type per interface name" in {
      val main = translated.main.value
      val mod  = moduleNamed(main, Seq("Splice", "Api", "Token", "HoldingV1"))
      mod.dataTypes.count(_.name == "Holding") shouldBe 1
    }

    "emit a record placeholder for an interface even when it also appears in mod.definitions" in {
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.data.Ref
      import com.digitalasset.daml.lf.language.Ast
      import com.digitalasset.daml.lf.language.LanguageVersion
      import com.digitalasset.daml.lf.data.ImmArray

      val ifaceName = DottedName.assertFromString("Holding")
      val ifaceDef = Ast.DDataType(
        serializable = false,
        params = ImmArray.empty,
        cons = Ast.DataInterface,
      )
      val ifaceSig = Ast.DefInterfaceSignature(
        requires = Set.empty,
        param = Ref.Name.assertFromString("this"),
        choices = Map.empty,
        methods = Map.empty,
        view = Ast.TBuiltin(Ast.BTUnit),
        coImplements = Map.empty,
      )
      val mod = Ast.ModuleSignature(
        name = ModuleName.assertFromString("Splice.Api.Token.HoldingV1"),
        definitions = Map(ifaceName -> ifaceDef),
        templates = Map.empty,
        exceptions = Map.empty,
        interfaces = Map(ifaceName -> ifaceSig),
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.PackageSignature(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-iface-in-defs"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val packageId = Ref.PackageId.assertFromString("synth-pkg-id")
      val dar = com.digitalasset.daml.lf.archive.Dar(
        main = packageId -> pkg,
        dependencies = List.empty,
      )
      val syntheticTranslated = AstToIntermediate.translate(dar)
      val syntheticMod = moduleNamed(syntheticTranslated.main.value, Seq("Splice", "Api", "Token", "HoldingV1"))
      syntheticMod.dataTypes.count(_.name == "Holding") shouldBe 1
      val placeholder = syntheticMod.dataTypes.find(_.name == "Holding").value
      placeholder.shape.isRecord shouldBe true
      placeholder.getRecord.fields shouldBe empty
      placeholder.isSerializable shouldBe false
    }

    "expose the Holding interface with a HoldingView viewtype" in {
      val main    = translated.main.value
      val module  = moduleNamed(main, Seq("Splice", "Api", "Token", "HoldingV1"))
      val iface   = module.interfaces
        .find(_.name == "Holding")
        .getOrElse(fail("interface Holding not found in module Splice.Api.Token.HoldingV1"))
      val viewSort = iface.viewType.value.sort
      viewSort.isTypeCon shouldBe true
      viewSort.typeCon.value.nameSegments shouldBe Seq("HoldingView")
    }

    "encode HoldingView as a record with expected fields" in {
      val main = translated.main.value
      val mod  = moduleNamed(main, Seq("Splice", "Api", "Token", "HoldingV1"))
      val holdingView = mod.dataTypes
        .find(d => d.name == "HoldingView" && d.shape.isRecord)
        .getOrElse(fail("record HoldingView not found"))
      val fields = holdingView.getRecord.fields.map(_.name).toSet
      fields should contain allOf ("instrumentId", "owner", "amount", "lock")
    }

    "encode Lock as a record carrying a List(Party) field" in {
      val main = translated.main.value
      val mod  = moduleNamed(main, Seq("Splice", "Api", "Token", "HoldingV1"))
      val lock = mod.dataTypes
        .find(d => d.name == "Lock" && d.shape.isRecord)
        .getOrElse(fail("record Lock not found"))
      val holders = lock.getRecord.fields
        .find(_.name == "holders")
        .getOrElse(fail("Lock.holders field not found"))
      holders.`type`.value.sort.isTypeApp shouldBe true
      val typeApp = holders.`type`.value.getTypeApp
      typeApp.function.value.sort.isBuiltin shouldBe true
      typeApp.function.value.getBuiltin shouldBe PbBuiltinType.BUILTIN_TYPE_LIST
      val argSort = typeApp.arguments.head.sort
      argSort.isBuiltin shouldBe true
      argSort.builtin.value shouldBe PbBuiltinType.BUILTIN_TYPE_PARTY
    }

    "include a variant somewhere in the dependency graph" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      val foundVariant = allPackages.exists(p =>
        p.modules.exists(m => m.dataTypes.exists(_.shape.isVariant))
      )
      foundVariant shouldBe true
    }

    "include an enum somewhere in the dependency graph" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      val foundEnum = allPackages.exists(p =>
        p.modules.exists(m => m.dataTypes.exists(_.shape.isEnumType))
      )
      foundEnum shouldBe true
    }

    "emit modules sorted by qualified name (load-bearing for byte-identical determinism)" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        val qualified = pkg.modules.map(_.nameSegments.mkString("."))
        qualified shouldBe qualified.sorted
      }
    }

    "emit data types within each module sorted by name" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        forAll(pkg.modules) { mod =>
          val names = mod.dataTypes.map(_.name)
          names shouldBe names.sorted
        }
      }
    }

    "emit templates within each module sorted by name" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        forAll(pkg.modules) { mod =>
          val names = mod.templates.map(_.name)
          names shouldBe names.sorted
        }
      }
    }

    "emit interfaces within each module sorted by name" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        forAll(pkg.modules) { mod =>
          val names = mod.interfaces.map(_.name)
          names shouldBe names.sorted
        }
      }
    }

    "emit interface methods sorted by name" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        forAll(pkg.modules) { mod =>
          forAll(mod.interfaces) { iface =>
            val names = iface.methods.map(_.name)
            names shouldBe names.sorted
          }
        }
      }
    }

    "emit choices within each template sorted by name" in {
      val allPackages = translated.main.toSeq ++ translated.dependencies
      forAll(allPackages) { pkg =>
        forAll(pkg.modules) { mod =>
          forAll(mod.templates) { tmpl =>
            val names = tmpl.choices.map(_.name)
            names shouldBe names.sorted
          }
        }
      }
    }

    "emit dependencies sorted by package id (load-bearing for byte-identical determinism)" in {
      val packageIds = translated.dependencies.map(_.packageId)
      packageIds shouldBe packageIds.sorted
    }

    "produce byte-identical output across repeated translations of the same DAR" in {
      val dar = SchemaDecoder.readDar(FixtureDar.toFile).getOrElse(
        fail(s"could not load fixture DAR at $FixtureDar")
      )
      val first  = AstToIntermediate.translate(dar).toByteArray
      val second = AstToIntermediate.translate(dar).toByteArray
      first.toSeq shouldBe second.toSeq
    }

    "translateTemplate emits name, sorted choices, keyType, sorted implements, and Static/Dynamic party analyses" in {
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.data.Ref
      import com.digitalasset.daml.lf.language.Ast
      import com.digitalasset.daml.lf.language.LanguageVersion

      import scala.collection.immutable.VectorMap

      val tmplName = DottedName.assertFromString("Tpl")
      val ifaceId = Ref.Identifier.assertFromString(
        "0000000000000000000000000000000000000000000000000000000000000001:Iface:Mod"
      )
      val ifaceBody = Ast.GenInterfaceInstanceBody[Unit](methods = Map.empty, view = ())
      val implementsEntry =
        Ast.GenTemplateImplements[Unit](interfaceId = ifaceId, body = ifaceBody)

      def choice(name: String): (Ref.ChoiceName, Ast.GenTemplateChoice[Unit]) = {
        val cname = Ref.ChoiceName.assertFromString(name)
        cname -> Ast.GenTemplateChoice[Unit](
          name = cname,
          consuming = true,
          controllers = (),
          choiceObservers = Some(()),
          choiceAuthorizers = None,
          selfBinder = Ref.Name.assertFromString("self"),
          argBinder = (Ref.Name.assertFromString("arg"), Ast.TBuiltin(Ast.BTUnit)),
          returnType = Ast.TBuiltin(Ast.BTUnit),
          update = (),
        )
      }

      val tmpl = Ast.GenTemplate[Unit](
        param = Ref.Name.assertFromString("this"),
        precond = (),
        signatories = (),
        choices = Map(choice("Zeta"), choice("Alpha")),
        observers = (),
        key = Some(
          Ast.GenTemplateKey[Unit](
            typ = Ast.TBuiltin(Ast.BTParty),
            body = (),
            maintainers = (),
          )
        ),
        implements = VectorMap(ifaceId -> implementsEntry),
      )

      val mod = Ast.ModuleSignature(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map.empty,
        templates = Map(tmplName -> tmpl),
        exceptions = Map.empty,
        interfaces = Map.empty,
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.PackageSignature(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-tmpl-pkg"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val packageId = Ref.PackageId.assertFromString(
        "0000000000000000000000000000000000000000000000000000000000000002"
      )
      val dar = com.digitalasset.daml.lf.archive.Dar(
        main = packageId -> pkg,
        dependencies = List.empty,
      )

      val dynamicTranslated = AstToIntermediate.translate(dar, PartyAnalyses.empty)
      val dynamicMod = moduleNamed(dynamicTranslated.main.value, Seq("Mod"))
      val dynamicTpl = dynamicMod.templates
        .find(_.name == "Tpl")
        .getOrElse(fail("template Tpl not found in synthetic module"))

      dynamicTpl.name shouldBe "Tpl"
      dynamicTpl.choices.map(_.name) shouldBe Seq("Alpha", "Zeta")
      dynamicTpl.keyType.value.sort.isBuiltin shouldBe true
      dynamicTpl.keyType.value.getBuiltin shouldBe PbBuiltinType.BUILTIN_TYPE_PARTY
      dynamicTpl.implements.map(_.packageId) shouldBe Seq(ifaceId.packageId)
      dynamicTpl.implements.head.moduleNameSegments shouldBe Seq("Iface")
      dynamicTpl.implements.head.nameSegments shouldBe Seq("Mod")
      dynamicTpl.signatories.value.shape.isDynamic shouldBe true
      dynamicTpl.observers.value.shape.isDynamic shouldBe true
      forAll(dynamicTpl.choices) { ch =>
        ch.controllers.value.shape.isDynamic shouldBe true
        ch.observers.value.shape.isDynamic shouldBe true
      }

      val staticAnalysis = TemplatePartyAnalysis(
        signatories = PartyAnalysisResult.Static(List("platform")),
        observers = PartyAnalysisResult.Static(List("watcher")),
        choices = Map(
          Ref.ChoiceName.assertFromString("Alpha") ->
            ChoicePartyAnalysis(
              controllers = PartyAnalysisResult.Static(List("controller")),
              observers = PartyAnalysisResult.Static(List("choiceObserver")),
            )
        ),
      )
      val populated = PartyAnalyses(
        Map((packageId, mod.name, tmplName) -> staticAnalysis)
      )

      val staticTranslated = AstToIntermediate.translate(dar, populated)
      val staticTpl = moduleNamed(staticTranslated.main.value, Seq("Mod")).templates
        .find(_.name == "Tpl")
        .getOrElse(fail("template Tpl not found in synthetic module (static run)"))

      staticTpl.signatories.value.getStatic.payloadFields shouldBe Seq("platform")
      staticTpl.observers.value.getStatic.payloadFields shouldBe Seq("watcher")
      val alpha = staticTpl.choices.find(_.name == "Alpha").value
      alpha.controllers.value.getStatic.payloadFields shouldBe Seq("controller")
      alpha.observers.value.getStatic.payloadFields shouldBe Seq("choiceObserver")
      val zeta = staticTpl.choices.find(_.name == "Zeta").value
      zeta.controllers.value.shape.isDynamic shouldBe true
      zeta.observers.value.shape.isDynamic shouldBe true
    }

    "map every Ast.BuiltinType to its proto BuiltinType counterpart" in {
      import com.digitalasset.daml.lf.archive.Dar
      import com.digitalasset.daml.lf.data.{ImmArray, Ref}
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.{Ast, LanguageVersion}

      val builtinExpectations: Seq[(Ast.BuiltinType, PbBuiltinType)] = Seq(
        Ast.BTUnit            -> PbBuiltinType.BUILTIN_TYPE_UNIT,
        Ast.BTBool            -> PbBuiltinType.BUILTIN_TYPE_BOOL,
        Ast.BTInt64           -> PbBuiltinType.BUILTIN_TYPE_INT64,
        Ast.BTText            -> PbBuiltinType.BUILTIN_TYPE_TEXT,
        Ast.BTNumeric         -> PbBuiltinType.BUILTIN_TYPE_NUMERIC,
        Ast.BTParty           -> PbBuiltinType.BUILTIN_TYPE_PARTY,
        Ast.BTDate            -> PbBuiltinType.BUILTIN_TYPE_DATE,
        Ast.BTTimestamp       -> PbBuiltinType.BUILTIN_TYPE_TIMESTAMP,
        Ast.BTList            -> PbBuiltinType.BUILTIN_TYPE_LIST,
        Ast.BTOptional        -> PbBuiltinType.BUILTIN_TYPE_OPTIONAL,
        Ast.BTTextMap         -> PbBuiltinType.BUILTIN_TYPE_TEXT_MAP,
        Ast.BTGenMap          -> PbBuiltinType.BUILTIN_TYPE_GEN_MAP,
        Ast.BTContractId      -> PbBuiltinType.BUILTIN_TYPE_CONTRACT_ID,
        Ast.BTAny             -> PbBuiltinType.BUILTIN_TYPE_ANY,
        Ast.BTTypeRep         -> PbBuiltinType.BUILTIN_TYPE_TYPE_REP,
        Ast.BTRoundingMode    -> PbBuiltinType.BUILTIN_TYPE_ROUNDING_MODE,
        Ast.BTBigNumeric      -> PbBuiltinType.BUILTIN_TYPE_BIGNUMERIC,
        Ast.BTAnyException    -> PbBuiltinType.BUILTIN_TYPE_ANY_EXCEPTION,
        Ast.BTUpdate          -> PbBuiltinType.BUILTIN_TYPE_UPDATE,
        Ast.BTScenario        -> PbBuiltinType.BUILTIN_TYPE_SCENARIO,
        Ast.BTArrow           -> PbBuiltinType.BUILTIN_TYPE_ARROW,
        Ast.BTFailureCategory -> PbBuiltinType.BUILTIN_TYPE_FAILURE_CATEGORY,
      )

      val recordName = DottedName.assertFromString("AllBuiltins")
      val fields = ImmArray(builtinExpectations.zipWithIndex.map { case ((bt, _), idx) =>
        Ref.Name.assertFromString(s"f$idx") -> (Ast.TBuiltin(bt): Ast.Type)
      }: _*)
      val record = Ast.DDataType(
        serializable = false,
        params = ImmArray.empty,
        cons = Ast.DataRecord(fields),
      )
      val mod = Ast.ModuleSignature(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map(recordName -> record),
        templates = Map.empty,
        exceptions = Map.empty,
        interfaces = Map.empty,
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.PackageSignature(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-builtins"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val dar = Dar(
        main = Ref.PackageId.assertFromString("synth-builtins-pkg") -> pkg,
        dependencies = List.empty,
      )
      val translated = AstToIntermediate.translate(dar)
      val emittedRecord = translated.main.value.modules.head.dataTypes.head.getRecord
      val emittedFieldNames = emittedRecord.fields.map(_.name)
      emittedFieldNames shouldBe builtinExpectations.indices.map(idx => s"f$idx")
      val emittedBuiltins = emittedRecord.fields.map(_.`type`.value.getBuiltin)
      emittedBuiltins shouldBe builtinExpectations.map(_._2)
    }

    "translate interface methods carrying name and return type" in {
      import com.digitalasset.daml.lf.archive.Dar
      import com.digitalasset.daml.lf.data.Ref
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.{Ast, LanguageVersion}

      val ifaceName = DottedName.assertFromString("HasOwner")
      val methodName = Ref.Name.assertFromString("getOwner")
      val ifaceSig = Ast.DefInterfaceSignature(
        requires = Set.empty,
        param = Ref.Name.assertFromString("this"),
        choices = Map.empty,
        methods = Map(methodName -> Ast.InterfaceMethod(methodName, Ast.TBuiltin(Ast.BTParty))),
        view = Ast.TBuiltin(Ast.BTUnit),
        coImplements = Map.empty,
      )
      val mod = Ast.ModuleSignature(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map.empty,
        templates = Map.empty,
        exceptions = Map.empty,
        interfaces = Map(ifaceName -> ifaceSig),
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.PackageSignature(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-iface-method"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val dar = Dar(
        main = Ref.PackageId.assertFromString("synth-iface-method-pkg") -> pkg,
        dependencies = List.empty,
      )
      val translated = AstToIntermediate.translate(dar)
      val iface = translated.main.value.modules.head.interfaces.head
      iface.methods should have size 1
      val method = iface.methods.head
      method.name shouldBe "getOwner"
      method.returnType.value.sort.isBuiltin shouldBe true
      method.returnType.value.getBuiltin shouldBe PbBuiltinType.BUILTIN_TYPE_PARTY
    }
  }
}
