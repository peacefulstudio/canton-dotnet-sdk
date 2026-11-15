// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.{Decode => LfDecode, DarReader => LfDarReader}
import com.digitalasset.daml.lf.language.Ast

import org.scalatest.{Inspectors, OptionValues}
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.nio.file.Paths

class SignatureErasureSpec extends AnyWordSpec with Matchers with OptionValues with Inspectors {

  private val FixtureDar = Paths.get(
    sys.props.getOrElse("jvmHelper.testFixtureDar",
      SpliceSnapshotDars.path("splice-api-token-holding-v1").toString
    )
  )

  private lazy val (fullPackage, signature) = {
    val dar = LfDarReader.readArchiveFromFile(FixtureDar.toFile)
      .toOption.getOrElse(fail(s"could not read fixture DAR at $FixtureDar"))
    val (_, fullPkg) = LfDecode
      .decodeArchivePayload(dar.main)
      .toOption
      .getOrElse(fail("could not decode main payload"))
    (fullPkg, SignatureErasure.erasePackage(fullPkg))
  }

  "SignatureErasure.erasePackage" should {

    "preserve languageVersion, metadata, directDeps, and imports verbatim" in {
      signature.languageVersion shouldBe fullPackage.languageVersion
      signature.metadata        shouldBe fullPackage.metadata
      signature.directDeps      shouldBe fullPackage.directDeps
      signature.imports         shouldBe fullPackage.imports
    }

    "preserve the module names" in {
      signature.modules.keySet shouldBe fullPackage.modules.keySet
    }

    "strip non-serializable DDataType definitions from every module" in {
      forAll(signature.modules.toSeq) { case (_, modSig) =>
        forAll(modSig.definitions.toSeq) { case (_, defn) =>
          defn match {
            case dt: Ast.DDataType => dt.serializable shouldBe true
            case _                 => succeed
          }
        }
      }
    }

    "strip non-serializable DDataType in a synthetic module while preserving serializable ones" in {
      import com.digitalasset.daml.lf.data.{ImmArray, Ref}
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.LanguageVersion
      val serialName    = DottedName.assertFromString("Kept")
      val nonSerialName = DottedName.assertFromString("Dropped")
      val serialDef     = Ast.DDataType(
        serializable = true,
        params = ImmArray.empty,
        cons = Ast.DataRecord(ImmArray.empty),
      )
      val nonSerialDef = Ast.DDataType(
        serializable = false,
        params = ImmArray.empty,
        cons = Ast.DataRecord(ImmArray.empty),
      )
      val mod = Ast.Module(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map(serialName -> serialDef, nonSerialName -> nonSerialDef),
        templates = Map.empty,
        exceptions = Map.empty,
        interfaces = Map.empty,
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.Package(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-filter"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val erased     = SignatureErasure.erasePackage(pkg)
      val erasedMod  = erased.modules(mod.name)
      erasedMod.definitions.keySet shouldBe Set(serialName)
    }

    "preserve serializable data type definitions verbatim" in {
      forAll(signature.modules.toSeq) { case (modName, modSig) =>
        val fullMod = fullPackage.modules(modName)
        forAll(modSig.definitions.toSeq) { case (defName, defn) =>
          (defn, fullMod.definitions(defName)) match {
            case (sigDt: Ast.DDataType, fullDt: Ast.DDataType) =>
              sigDt shouldBe fullDt
            case _ => succeed
          }
        }
      }
    }

    "convert GenDValue into DValueSignature with body erased to unit" in {
      val anyValueDef = signature.modules.values.toSeq
        .flatMap(_.definitions.values)
        .collectFirst { case v: Ast.DValueSignature => v }
      anyValueDef match {
        case Some(v) => v.body shouldBe ((): Unit)
        case None    => succeed
      }
    }

    "preserve template names and choice counts on every module" in {
      forAll(signature.modules.toSeq) { case (modName, modSig) =>
        val fullMod = fullPackage.modules(modName)
        modSig.templates.keySet shouldBe fullMod.templates.keySet
        forAll(modSig.templates.toSeq) { case (tname, tsig) =>
          tsig.choices.keySet shouldBe fullMod.templates(tname).choices.keySet
        }
      }
    }

    "preserve interface names and method names on every module" in {
      forAll(signature.modules.toSeq) { case (modName, modSig) =>
        val fullMod = fullPackage.modules(modName)
        modSig.interfaces.keySet shouldBe fullMod.interfaces.keySet
        forAll(modSig.interfaces.toSeq) { case (iname, isig) =>
          isig.methods.keySet shouldBe fullMod.interfaces(iname).methods.keySet
        }
      }
    }

    "carry exceptions through as DefExceptionSignature placeholders" in {
      forAll(signature.modules.toSeq) { case (modName, modSig) =>
        val fullMod = fullPackage.modules(modName)
        modSig.exceptions.keySet shouldBe fullMod.exceptions.keySet
        forAll(modSig.exceptions.values.toSeq) { exn =>
          exn shouldBe Ast.DefExceptionSignature
        }
      }
    }

    "erase template precond, signatories, observers, and choice update bodies to unit" in {
      import com.digitalasset.daml.lf.data.{ImmArray, Ref}
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.LanguageVersion

      val tplName    = DottedName.assertFromString("Tpl")
      val paramName  = Ref.Name.assertFromString("this")
      val choiceName = Ref.ChoiceName.assertFromString("Do")
      val tparty     = Ast.TBuiltin(Ast.BTParty)
      val tunit      = Ast.TBuiltin(Ast.BTUnit)
      val dummyExpr  = Ast.EBuiltinCon(Ast.BCTrue)

      val choice = Ast.TemplateChoice(
        name = choiceName,
        consuming = true,
        controllers = dummyExpr,
        choiceObservers = Some(dummyExpr),
        choiceAuthorizers = None,
        selfBinder = Ref.Name.assertFromString("self"),
        argBinder = Ref.Name.assertFromString("arg") -> tunit,
        returnType = tunit,
        update = dummyExpr,
      )
      val template = Ast.Template(
        param = paramName,
        precond = dummyExpr,
        signatories = dummyExpr,
        choices = Map(choiceName -> choice),
        observers = dummyExpr,
        key = Some(Ast.TemplateKey(typ = tparty, body = dummyExpr, maintainers = dummyExpr)),
        implements = scala.collection.immutable.VectorMap.empty,
      )
      val dataDef = Ast.DDataType(
        serializable = true,
        params = ImmArray.empty,
        cons = Ast.DataRecord(ImmArray(Ref.Name.assertFromString("owner") -> (tparty: Ast.Type))),
      )
      val mod = Ast.Module(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map(tplName -> dataDef),
        templates = Map(tplName -> template),
        exceptions = Map.empty,
        interfaces = Map.empty,
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.Package(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-template"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val erased = SignatureErasure.erasePackage(pkg)
      val erasedModule   = erased.modules(mod.name)
      val erasedTemplate = erasedModule.templates(tplName)
      erasedTemplate.param        shouldBe paramName
      erasedTemplate.precond      shouldBe ((): Unit)
      erasedTemplate.signatories  shouldBe ((): Unit)
      erasedTemplate.observers    shouldBe ((): Unit)
      erasedTemplate.key.value.typ shouldBe tparty
      erasedTemplate.key.value.body shouldBe ((): Unit)
      erasedTemplate.key.value.maintainers shouldBe ((): Unit)
      val erasedChoice = erasedTemplate.choices(choiceName)
      erasedChoice.controllers        shouldBe ((): Unit)
      erasedChoice.choiceObservers    shouldBe Some(())
      erasedChoice.update             shouldBe ((): Unit)
      erasedChoice.returnType         shouldBe tunit
      erasedChoice.consuming          shouldBe true
    }

    "erase interface coImplements body methods and view to unit while preserving method names" in {
      import com.digitalasset.daml.lf.data.Ref
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.LanguageVersion

      val ifaceName      = DottedName.assertFromString("HasOwner")
      val targetPkgId    = Ref.PackageId.assertFromString("synth-target-pkg")
      val targetTplId    = Ref.Identifier(
        targetPkgId,
        Ref.QualifiedName.assertFromString("Target:Tpl"),
      )
      val methodName     = Ref.Name.assertFromString("getOwner")
      val dummyExpr      = Ast.EBuiltinCon(Ast.BCTrue)
      val coImpl = Ast.InterfaceCoImplements(
        templateId = targetTplId,
        body = Ast.InterfaceInstanceBody(
          methods = Map(methodName -> Ast.InterfaceInstanceMethod(methodName, dummyExpr)),
          view = dummyExpr,
        ),
      )
      val iface = Ast.DefInterface(
        requires = Set.empty,
        param = Ref.Name.assertFromString("this"),
        choices = Map.empty,
        methods = Map(methodName -> Ast.InterfaceMethod(methodName, Ast.TBuiltin(Ast.BTParty))),
        view = Ast.TBuiltin(Ast.BTUnit),
        coImplements = Map(targetTplId -> coImpl),
      )
      val mod = Ast.Module(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map.empty,
        templates = Map.empty,
        exceptions = Map.empty,
        interfaces = Map(ifaceName -> iface),
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.Package(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-iface-coimpl"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val erased = SignatureErasure.erasePackage(pkg)
      val erasedIface = erased.modules(mod.name).interfaces(ifaceName)
      val erasedCoImpl = erasedIface.coImplements(targetTplId)
      erasedCoImpl.templateId          shouldBe targetTplId
      erasedCoImpl.body.view           shouldBe ((): Unit)
      erasedCoImpl.body.methods.keySet shouldBe Set(methodName)
      val erasedMethod = erasedCoImpl.body.methods(methodName)
      erasedMethod.name shouldBe methodName
      erasedMethod.value shouldBe ((): Unit)
    }

    "erase template implements body methods and view to unit while preserving interface id" in {
      import com.digitalasset.daml.lf.data.{ImmArray, Ref}
      import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName}
      import com.digitalasset.daml.lf.language.LanguageVersion

      val tplName     = DottedName.assertFromString("Tpl")
      val ifaceTplId  = Ref.Identifier(
        Ref.PackageId.assertFromString("synth-iface-pkg"),
        Ref.QualifiedName.assertFromString("Iface:HasOwner"),
      )
      val methodName  = Ref.Name.assertFromString("getOwner")
      val dummyExpr   = Ast.EBuiltinCon(Ast.BCTrue)
      val tparty      = Ast.TBuiltin(Ast.BTParty)

      val impl = Ast.TemplateImplements(
        interfaceId = ifaceTplId,
        body = Ast.InterfaceInstanceBody(
          methods = Map(methodName -> Ast.InterfaceInstanceMethod(methodName, dummyExpr)),
          view = dummyExpr,
        ),
      )
      val template = Ast.Template(
        param = Ref.Name.assertFromString("this"),
        precond = dummyExpr,
        signatories = dummyExpr,
        choices = Map.empty,
        observers = dummyExpr,
        key = None,
        implements = scala.collection.immutable.VectorMap(ifaceTplId -> impl),
      )
      val dataDef = Ast.DDataType(
        serializable = true,
        params = ImmArray.empty,
        cons = Ast.DataRecord(ImmArray(Ref.Name.assertFromString("owner") -> (tparty: Ast.Type))),
      )
      val mod = Ast.Module(
        name = ModuleName.assertFromString("Mod"),
        definitions = Map(tplName -> dataDef),
        templates = Map(tplName -> template),
        exceptions = Map.empty,
        interfaces = Map.empty,
        featureFlags = Ast.FeatureFlags.default,
      )
      val pkg = Ast.Package(
        modules = Map(mod.name -> mod),
        directDeps = Set.empty,
        languageVersion = LanguageVersion.defaultLfVersion,
        metadata = Ast.PackageMetadata(
          name = Ref.PackageName.assertFromString("synth-tpl-impl"),
          version = Ref.PackageVersion.assertFromString("0.0.0"),
          upgradedPackageId = None,
        ),
        imports = Ast.DeclaredImports(Set.empty),
      )
      val erased = SignatureErasure.erasePackage(pkg)
      val erasedTpl = erased.modules(mod.name).templates(tplName)
      erasedTpl.implements.keySet shouldBe Set(ifaceTplId)
      val erasedImpl = erasedTpl.implements(ifaceTplId)
      erasedImpl.interfaceId            shouldBe ifaceTplId
      erasedImpl.body.view              shouldBe ((): Unit)
      erasedImpl.body.methods.keySet    shouldBe Set(methodName)
      erasedImpl.body.methods(methodName).value shouldBe ((): Unit)
    }
  }
}
