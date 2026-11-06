// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.Dar
import com.digitalasset.daml.lf.data.Ref.{DottedName, ModuleName, PackageId}
import com.digitalasset.daml.lf.language.Ast

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.{Constant => PbConstant}

/** Top-level values whose whole body is one Text, Int64, Bool or Numeric
  * literal, keyed by `(packageId, moduleName)`. Produced by
  * [[LiteralConstants.compute]] from the fully-decoded DAR, because
  * [[SignatureErasure]] drops every value body.
  */
final case class LiteralConstants(byModule: Map[(PackageId, ModuleName), Seq[PbConstant]]) {
  def lookup(packageId: PackageId, moduleName: ModuleName): Seq[PbConstant] =
    byModule.getOrElse((packageId, moduleName), Seq.empty)
}

object LiteralConstants {

  val empty: LiteralConstants = LiteralConstants(Map.empty)

  private val UserWrittenName = """(?:[A-Za-z0-9_]|\$u[0-9a-f]{4}|\$U[0-9a-f]{8})+""".r

  def compute(dar: Dar[(PackageId, Ast.Package)]): LiteralConstants =
    LiteralConstants(
      (dar.main +: dar.dependencies).flatMap { case (packageId, pkg) =>
        pkg.modules.map { case (moduleName, module) =>
          (packageId, moduleName) -> fromDefinitions(module.definitions)
        }
      }.toMap
    )

  /** Only a value a developer wrote qualifies: a compiler-minted name
    * (`$$cshow`, `$$maxtag_...`: damlc escapes the `$` of every
    * compiler name as `$$`), a multi-segment name, a generic value (its body is
    * a type abstraction), a computation, a reference to another value and
    * every other literal kind (unit, date, timestamp) are left out. The
    * result is sorted by ordinal name, matching the .NET parser.
    */
  def fromDefinitions(definitions: Iterable[(DottedName, Ast.Definition)]): Seq[PbConstant] =
    definitions.toSeq
      .collect { case (name, Ast.DValue(_, body)) => (name, body) }
      .flatMap { case (name, body) =>
        userWrittenName(name).flatMap(valueName => literalOf(body).map(PbConstant(valueName, _)))
      }
      .sortBy(_.name)

  private def userWrittenName(name: DottedName): Option[String] =
    Some(name.dottedName).filter(spelling =>
      name.segments.length == 1 && UserWrittenName.pattern.matcher(spelling).matches()
    )

  private def literalOf(body: Ast.Expr): Option[PbConstant.Literal] =
    body match {
      case Ast.ELocation(_, located)             => literalOf(located)
      case Ast.EBuiltinLit(Ast.BLInt64(value))   => Some(PbConstant.Literal.Int64Value(value))
      case Ast.EBuiltinLit(Ast.BLText(value))    => Some(PbConstant.Literal.TextValue(value))
      case Ast.EBuiltinLit(Ast.BLNumeric(value)) => Some(PbConstant.Literal.NumericValue(value.toPlainString))
      case Ast.EBuiltinCon(Ast.BCTrue)           => Some(PbConstant.Literal.BoolValue(true))
      case Ast.EBuiltinCon(Ast.BCFalse)          => Some(PbConstant.Literal.BoolValue(false))
      case _                                     => None
    }
}
