// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.Dar
import com.digitalasset.daml.lf.data.Ref.{
  DottedName,
  Identifier,
  PackageId,
  TypeConId,
}
import com.digitalasset.daml.lf.language.Ast

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.{
  BuiltinType => PbBuiltinType,
  Choice => PbChoice,
  DataType => PbDataType,
  DynamicParties => PbDynamicParties,
  Enum => PbEnum,
  Field => PbField,
  IntermediateDar => PbDar,
  IntermediateModule => PbModule,
  IntermediatePackage => PbPackage,
  Interface => PbInterface,
  InterfaceMethod => PbInterfaceMethod,
  PartyAnalysis => PbPartyAnalysis,
  Record => PbRecord,
  StaticParties => PbStaticParties,
  Template => PbTemplate,
  Type => PbType,
  TypeApp => PbTypeApp,
  TypeConName => PbTypeConId,
  Variant => PbVariant,
}

/** Maps `Dar[(PackageId, Ast.PackageSignature)]` to an `IntermediateDar`
  * protobuf message.
  *
  * This is the only place in the project that depends on
  * `daml-lf-archive` Scala case-class shapes (alongside its sibling
  * [[SignatureErasure]]). When DA renames an internal case class, only
  * these two files rebase; the on-disk protobuf wire format is owned
  * by this repo and stays stable.
  *
  * Planned swap: replace `translate` with a `SchemaVisitor` driven by
  * upstream `com.digitalasset.transcode.daml_lf.LfSchemaProcessor` (DA's
  * `transcode-codegen` library, expected on the `digital-asset` GitHub org
  * by ~2026-07-22 per a DA contributor's note). The visitor's emit
  * callbacks build the same `IntermediateDar` proto, so this file becomes
  * a thin visitor implementation and `SchemaDecoder` collapses to the
  * upstream call.
  */
object AstToIntermediate {

  def translate(
      dar: Dar[(PackageId, Ast.PackageSignature)],
      analyses: PartyAnalyses = PartyAnalyses.empty,
      constants: LiteralConstants = LiteralConstants.empty,
  ): PbDar =
    PbDar(
      main = Some(translatePackage(dar.main._1, dar.main._2, analyses, constants)),
      dependencies = dar.dependencies
        .map { case (id, pkg) => translatePackage(id, pkg, analyses, constants) }
        .sortBy(_.packageId),
    )

  private def translatePackage(
      packageId: PackageId,
      pkg: Ast.PackageSignature,
      analyses: PartyAnalyses,
      constants: LiteralConstants,
  ): PbPackage =
    PbPackage(
      packageId = packageId,
      packageName = pkg.metadata.name,
      packageVersion = pkg.metadata.version.toString,
      languageVersion = pkg.languageVersion.pretty,
      modules = pkg.modules.toSeq
        .sortBy { case (n, _) => n.toString }
        .map { case (_, m) => translateModule(packageId, m, analyses, constants) },
      upgradedPackageId = pkg.metadata.upgradedPackageId.getOrElse(""),
    )

  private def translateModule(
      packageId: PackageId,
      mod: Ast.ModuleSignature,
      analyses: PartyAnalyses,
      constants: LiteralConstants,
  ): PbModule = {
    val declaredDataTypes = mod.definitions.toSeq.collect {
      case (name, dt: Ast.DDataType) => translateDataType(name, dt)
    }
    val declaredNames = declaredDataTypes.map(_.name).toSet
    val interfacePlaceholderFallbacks = mod.interfaces.toSeq.collect {
      case (name, _) if !declaredNames.contains(dottedNameSegments(name).mkString(".")) =>
        interfacePlaceholderRecord(name)
    }
    PbModule(
      nameSegments = dottedNameSegments(mod.name),
      dataTypes = (declaredDataTypes ++ interfacePlaceholderFallbacks)
        .sortBy(_.name),
      templates = mod.templates.toSeq
        .sortBy { case (n, _) => n.toString }
        .map { case (n, t) => translateTemplate(n, t, analyses.lookup(packageId, mod.name, n)) },
      interfaces = mod.interfaces.toSeq
        .sortBy { case (n, _) => n.toString }
        .map { case (n, i) => translateInterface(n, i) },
      constants = constants.lookup(packageId, mod.name),
    )
  }

  private def interfacePlaceholderRecord(name: DottedName): PbDataType =
    PbDataType(
      name = dottedNameSegments(name).mkString("."),
      typeParameters = Seq.empty,
      isSerializable = false,
      shape = PbDataType.Shape.Record(PbRecord(fields = Seq.empty)),
    )

  private def translateDataType(name: DottedName, dt: Ast.DDataType): PbDataType = dt.cons match {
    case Ast.DataInterface =>
      interfacePlaceholderRecord(name)
    case Ast.DataRecord(fields) =>
      declaredDataType(name, dt, PbDataType.Shape.Record(
        PbRecord(fields = fields.toSeq.map { case (n, t) => PbField(n, Some(translateType(t))) })
      ))
    case Ast.DataVariant(variants) =>
      declaredDataType(name, dt, PbDataType.Shape.Variant(
        PbVariant(constructors =
          variants.toSeq.map { case (n, t) => PbField(n, Some(translateType(t))) }
        )
      ))
    case Ast.DataEnum(constructors) =>
      declaredDataType(name, dt, PbDataType.Shape.EnumType(PbEnum(constructors = constructors.toSeq)))
  }

  private def declaredDataType(
      name: DottedName,
      dt: Ast.DDataType,
      shape: PbDataType.Shape,
  ): PbDataType =
    PbDataType(
      name = dottedNameSegments(name).mkString("."),
      typeParameters = dt.params.toSeq.map { case (n, _) => n: String },
      isSerializable = dt.serializable,
      shape = shape,
    )

  private def translateTemplate(
      name: DottedName,
      tmpl: Ast.TemplateSignature,
      analysis: TemplatePartyAnalysis,
  ): PbTemplate =
    PbTemplate(
      name = dottedNameSegments(name).mkString("."),
      choices = tmpl.choices.toSeq
        .sortBy { case (n, _) => n: String }
        .map { case (cname, c) =>
          translateChoice(c, analysis.choices.getOrElse(cname, ChoicePartyAnalysis.dynamic))
        },
      keyType = tmpl.key.map(k => translateType(k.typ)),
      implements = tmpl.implements.keys.toSeq
        .map(translateTypeConId)
        .sortBy(tcn =>
          (tcn.packageId, tcn.moduleNameSegments.mkString("."), tcn.nameSegments.mkString("."))
        ),
      signatories = Some(translatePartyAnalysis(analysis.signatories)),
      observers = Some(translatePartyAnalysis(analysis.observers)),
    )

  private def translateInterface(
      name: DottedName,
      iface: Ast.DefInterfaceSignature,
  ): PbInterface =
    PbInterface(
      name = dottedNameSegments(name).mkString("."),
      viewType = Some(translateType(iface.view)),
      methods = iface.methods.toSeq
        .sortBy { case (n, _) => n: String }
        .map { case (_, m) => translateInterfaceMethod(m) },
      choices = iface.choices.toSeq
        .sortBy { case (n, _) => n: String }
        .map { case (_, c) => translateChoice(c, ChoicePartyAnalysis.dynamic) },
    )

  private def translateInterfaceMethod(m: Ast.InterfaceMethod): PbInterfaceMethod =
    PbInterfaceMethod(name = m.name, returnType = Some(translateType(m.returnType)))

  private def translateChoice(
      c: Ast.TemplateChoiceSignature,
      analysis: ChoicePartyAnalysis,
  ): PbChoice =
    PbChoice(
      name = c.name,
      consuming = c.consuming,
      argumentType = Some(translateType(c.argBinder._2)),
      returnType = Some(translateType(c.returnType)),
      controllers = Some(translatePartyAnalysis(analysis.controllers)),
      observers = Some(translatePartyAnalysis(analysis.observers)),
    )

  private def translatePartyAnalysis(result: PartyAnalysisResult): PbPartyAnalysis = result match {
    case PartyAnalysisResult.Static(fields) =>
      PbPartyAnalysis(shape =
        PbPartyAnalysis.Shape.Static(PbStaticParties(payloadFields = fields))
      )
    case PartyAnalysisResult.Dynamic =>
      PbPartyAnalysis(shape = PbPartyAnalysis.Shape.Dynamic(PbDynamicParties()))
  }

  private def translateType(typ: Ast.Type): PbType = {
    val sort: PbType.Sort = typ match {
      case Ast.TBuiltin(bt) => PbType.Sort.Builtin(translateBuiltin(bt))
      case Ast.TTyCon(tc)   => PbType.Sort.TypeCon(translateTypeConId(tc))
      case Ast.TApp(fun, _) =>
        val (head, args) = collectApp(typ, Nil)
        PbType.Sort.TypeApp(
          PbTypeApp(function = Some(translateType(head)), arguments = args.map(translateType))
        )
      case Ast.TVar(name) => PbType.Sort.TypeVar(name)
      case Ast.TNat(n)    => PbType.Sort.Nat(n.toLong)
      case Ast.TSynApp(_, _) | Ast.TForall(_, _) | Ast.TStruct(_) =>
        throw new IllegalStateException(
          s"Unsupported type in schema-mode payload: ${typ.pretty}"
        )
    }
    PbType(sort = sort)
  }

  @scala.annotation.tailrec
  private def collectApp(t: Ast.Type, acc: List[Ast.Type]): (Ast.Type, List[Ast.Type]) =
    t match {
      case Ast.TApp(fun, arg) => collectApp(fun, arg :: acc)
      case head               => (head, acc)
    }

  private def translateTypeConId(tcn: TypeConId): PbTypeConId = {
    val ident: Identifier = tcn
    PbTypeConId(
      packageId = ident.packageId,
      moduleNameSegments = dottedNameSegments(ident.qualifiedName.module),
      nameSegments = dottedNameSegments(ident.qualifiedName.name),
    )
  }

  private def dottedNameSegments(dn: DottedName): Seq[String] =
    dn.segments.toSeq.map(s => s: String)

  private def translateBuiltin(bt: Ast.BuiltinType): PbBuiltinType = bt match {
    case Ast.BTUnit            => PbBuiltinType.BUILTIN_TYPE_UNIT
    case Ast.BTBool            => PbBuiltinType.BUILTIN_TYPE_BOOL
    case Ast.BTInt64           => PbBuiltinType.BUILTIN_TYPE_INT64
    case Ast.BTText            => PbBuiltinType.BUILTIN_TYPE_TEXT
    case Ast.BTNumeric         => PbBuiltinType.BUILTIN_TYPE_NUMERIC
    case Ast.BTParty           => PbBuiltinType.BUILTIN_TYPE_PARTY
    case Ast.BTDate            => PbBuiltinType.BUILTIN_TYPE_DATE
    case Ast.BTTimestamp       => PbBuiltinType.BUILTIN_TYPE_TIMESTAMP
    case Ast.BTList            => PbBuiltinType.BUILTIN_TYPE_LIST
    case Ast.BTOptional        => PbBuiltinType.BUILTIN_TYPE_OPTIONAL
    case Ast.BTTextMap         => PbBuiltinType.BUILTIN_TYPE_TEXT_MAP
    case Ast.BTGenMap          => PbBuiltinType.BUILTIN_TYPE_GEN_MAP
    case Ast.BTContractId      => PbBuiltinType.BUILTIN_TYPE_CONTRACT_ID
    case Ast.BTAny             => PbBuiltinType.BUILTIN_TYPE_ANY
    case Ast.BTTypeRep         => PbBuiltinType.BUILTIN_TYPE_TYPE_REP
    case Ast.BTRoundingMode    => PbBuiltinType.BUILTIN_TYPE_ROUNDING_MODE
    case Ast.BTBigNumeric      => PbBuiltinType.BUILTIN_TYPE_BIGNUMERIC
    case Ast.BTAnyException    => PbBuiltinType.BUILTIN_TYPE_ANY_EXCEPTION
    case Ast.BTUpdate          => PbBuiltinType.BUILTIN_TYPE_UPDATE
    case Ast.BTScenario        => PbBuiltinType.BUILTIN_TYPE_SCENARIO
    case Ast.BTArrow           => PbBuiltinType.BUILTIN_TYPE_ARROW
    case Ast.BTFailureCategory => PbBuiltinType.BUILTIN_TYPE_FAILURE_CATEGORY
  }
}
