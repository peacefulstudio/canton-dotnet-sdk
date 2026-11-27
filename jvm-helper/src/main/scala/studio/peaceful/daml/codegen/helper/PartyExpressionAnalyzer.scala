// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.data.Ref
import com.digitalasset.daml.lf.language.Ast

import scala.annotation.tailrec
import scala.collection.mutable

/** Verdict for a `List Party`-typed Daml-LF expression.
  *
  * [[PartyAnalysisResult.Static]] carries the template payload fields, in
  * declaration order, that every element of the list projects from the
  * template parameter; the empty list is a valid Static verdict.
  * [[PartyAnalysisResult.Dynamic]] means at least one element could not be
  * resolved to such a projection.
  */
sealed trait PartyAnalysisResult extends Product with Serializable
object PartyAnalysisResult {
  final case class Static(payloadFields: List[String]) extends PartyAnalysisResult
  case object Dynamic                                  extends PartyAnalysisResult
}

/** Classifies a `List Party`-typed Daml-LF expression as a
  * [[PartyAnalysisResult.Static]] list of template payload-field projections
  * or as [[PartyAnalysisResult.Dynamic]].
  */
object PartyExpressionAnalyzer {

  type ValueResolver = Ref.ValueRef => Option[Ast.Expr]

  /** A party-expression walk stops once its depth passes this, and an
    * application or type-application spine stops once its link count does,
    * resolving to [[PartyAnalysisResult.Dynamic]]. It mirrors the C# DarParser's
    * bound of the same value, so both Intermediate DAR producers give the same
    * verdict on a clause nested past it instead of this one reporting Static or
    * overflowing the stack.
    */
  private val MaxExpressionDepth = 256

  def analyze(
      expr: Ast.Expr,
      templateParam: String,
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
  ): PartyAnalysisResult = {
    if (expr == null) return PartyAnalysisResult.Dynamic
    val payloadFields = mutable.ListBuffer.empty[String]
    if (
      collectPayloadFields(
        expr,
        templateParam,
        payloadFields,
        resolveValue,
        currentPackageId,
        throughValueIndirection = true,
        depth = 0,
      )
    )
      PartyAnalysisResult.Static(payloadFields.toList)
    else PartyAnalysisResult.Dynamic
  }

  private def collectPayloadFields(
      expr: Ast.Expr,
      templateParam: String,
      acc: mutable.ListBuffer[String],
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
      throughValueIndirection: Boolean,
      depth: Int,
  ): Boolean = depth <= MaxExpressionDepth && (stripLocations(expr) match {
    case _: Ast.ENil =>
      true

    case Ast.ECons(_, front, tail) =>
      front.toSeq.forall(collectPayloadFieldProjection(_, templateParam, acc)) &&
        collectPayloadFields(tail, templateParam, acc, resolveValue, currentPackageId, throughValueIndirection, depth + 1)

    case app: Ast.EApp if throughValueIndirection =>
      resolvePartyValueIndirection(app, templateParam, acc, resolveValue, currentPackageId, depth + 1)

    case _ => false
  })

  private sealed trait PartyBinding
  private object PartyBinding {
    case object Opaque                           extends PartyBinding
    case object TemplateParam                    extends PartyBinding
    final case class PayloadField(field: String) extends PartyBinding
  }

  /** Resolves the `signatory <field>` / `observer <field>` / `controller
    * <field>` indirection this SDK compiles to an application of a synthesized
    * value to the template parameter — `App(Val(<self>), [this])` at template
    * level and `App(Val(<self>), [this, arg])` per choice. The defining
    * expression resolves either by reducing to an imported single-`Party` list
    * wrapper applied to a bound template payload field, or, at the
    * template-level arity only, by being a literal list of payload-field
    * projections with no further value indirection. Every other shape stays
    * Dynamic.
    *
    * Two names in `DA.Internal.Template.Functions` denote that wrapper: the
    * class selector `$$ctoParties` damlc synthesizes for the single-`Party`
    * instance, typed `Party -> [Party]`, and `toParties`, the overloaded class
    * method that selector is drawn from. The numbered selectors
    * `$$ctoParties1` and `$$ctoParties2` are the `[Party]` and `Optional Party`
    * instances, so resolving through them would name a field of the wrong type
    * as a single party; neither name is matched.
    */
  private def resolvePartyValueIndirection(
      app: Ast.EApp,
      templateParam: String,
      acc: mutable.ListBuffer[String],
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
      depth: Int,
  ): Boolean = {
    val resolvedIndirection =
      if (depth > MaxExpressionDepth) None
      else
        for {
          (head, args) <- flattenApplication(app)
          contractArg  <- args.headOption.map(stripLocations)
          if isVariable(contractArg, templateParam)
          ref <- unwrapTypeApplications(head).collect { case Ast.EVal(ref) => ref }
          (binder, body) <- resolveValue(ref).map(stripLocations).collect {
            case Ast.EAbs((binder, _), body) => (binder, body)
          }
        } yield (ref, args, binder, body)

    resolvedIndirection.exists { case (ref, args, binder, body) =>
      val environment =
        mutable.Map[String, PartyBinding](binder.toString -> PartyBinding.TemplateParam)
      reduceSingletonPartyField(
        body,
        environment,
        resolveValue,
        currentPackageId,
        mutable.Set[Ref.ValueRef](ref),
        depth + 1,
      ) match {
        case Some(field) => acc += field; true
        case None =>
          val isTemplateLevelIndirection = args.length == 1
          isTemplateLevelIndirection && collectPayloadFields(
            body,
            binder.toString,
            acc,
            resolveValue,
            currentPackageId,
            throughValueIndirection = false,
            depth + 1,
          )
      }
    }
  }

  private def isVariable(expr: Ast.Expr, name: String): Boolean = expr match {
    case Ast.EVar(variable) => variable.toString == name
    case _                  => false
  }

  private def flattenApplication(expr: Ast.Expr): Option[(Ast.Expr, List[Ast.Expr])] = {
    @tailrec
    def go(e: Ast.Expr, acc: List[Ast.Expr], links: Int): Option[(Ast.Expr, List[Ast.Expr])] =
      stripLocations(e) match {
        case Ast.EApp(_, _) if links >= MaxExpressionDepth => None
        case Ast.EApp(fun, arg)                            => go(fun, arg :: acc, links + 1)
        case other                                         => Some((other, acc))
      }
    go(expr, Nil, 0)
  }

  /** Reduces an expression the analyzer expects to yield a single-element `List
    * Party` to the payload field that element projects, walking `Let` and `Abs`
    * binders and unfolding same-package value applications. `unfoldedValues`
    * holds the values already unfolded on this path, so a value that applies
    * itself, directly or through a mutual chain, reduces to no field instead of
    * spinning.
    */
  private def reduceSingletonPartyField(
      expr: Ast.Expr,
      environment: mutable.Map[String, PartyBinding],
      resolveValue: ValueResolver,
      currentPackageId: Ref.PackageId,
      unfoldedValues: mutable.Set[Ref.ValueRef],
      depth: Int,
  ): Option[String] = if (depth > MaxExpressionDepth) None else stripLocations(expr) match {
    case Ast.ELet(binding, body) =>
      binding.binder.foreach(name =>
        environment(name.toString) = resolvePartyBinding(binding.bound, environment)
      )
      reduceSingletonPartyField(body, environment, resolveValue, currentPackageId, unfoldedValues, depth + 1)

    case Ast.EAbs((binder, _), body) =>
      environment(binder.toString) = PartyBinding.Opaque
      reduceSingletonPartyField(body, environment, resolveValue, currentPackageId, unfoldedValues, depth + 1)

    case Ast.EApp(callee, arg) =>
      unwrapTypeApplications(callee) match {
        case Some(Ast.EVal(ref)) if isImportedToParties(ref, currentPackageId) =>
          resolvePartyBinding(arg, environment) match {
            case PartyBinding.PayloadField(field) => Some(field)
            case _                                => None
          }

        case Some(Ast.EVal(ref)) if ref.packageId == currentPackageId && !unfoldedValues.contains(ref) =>
          unfoldedValues += ref
          val argumentBinding = resolvePartyBinding(arg, environment)
          resolveValue(ref).map(stripLocations).flatMap {
            case Ast.EAbs((binder, _), body) =>
              reduceSingletonPartyField(
                body,
                mutable.Map[String, PartyBinding](binder.toString -> argumentBinding),
                resolveValue,
                currentPackageId,
                unfoldedValues,
                depth + 1,
              )
            case _ => None
          }

        case _ => None
      }

    case _ => None
  }

  private def resolvePartyBinding(
      expr: Ast.Expr,
      environment: mutable.Map[String, PartyBinding],
  ): PartyBinding = stripLocations(expr) match {
    case Ast.EVar(name) =>
      environment.getOrElse(name.toString, PartyBinding.Opaque)

    case Ast.ERecProj(_, field, record) =>
      stripLocations(record) match {
        case Ast.EVar(name) if environment.get(name.toString).contains(PartyBinding.TemplateParam) =>
          PartyBinding.PayloadField(field.toString)
        case _ => PartyBinding.Opaque
      }

    case _ => PartyBinding.Opaque
  }

  private val SinglePartyToPartiesNames: Set[String] = Set("toParties", "$$ctoParties")

  private def isImportedToParties(ref: Ref.ValueRef, currentPackageId: Ref.PackageId): Boolean =
    ref.packageId != currentPackageId &&
      ref.qualifiedName.module.toString == "DA.Internal.Template.Functions" &&
      SinglePartyToPartiesNames.contains(ref.qualifiedName.name.toString)

  @tailrec
  private def stripLocations(expr: Ast.Expr): Ast.Expr = expr match {
    case Ast.ELocation(_, inner) => stripLocations(inner)
    case other                   => other
  }

  private def unwrapTypeApplications(expr: Ast.Expr): Option[Ast.Expr] = {
    @tailrec
    def go(e: Ast.Expr, links: Int): Option[Ast.Expr] = stripLocations(e) match {
      case Ast.ETyApp(_, _) if links >= MaxExpressionDepth => None
      case Ast.ETyApp(inner, _)                            => go(inner, links + 1)
      case other                                           => Some(other)
    }
    go(expr, 0)
  }

  private def collectPayloadFieldProjection(
      expr: Ast.Expr,
      templateParam: String,
      acc: mutable.ListBuffer[String],
  ): Boolean = stripLocations(expr) match {
    case Ast.ERecProj(_, field, record) =>
      stripLocations(record) match {
        case Ast.EVar(name) if name.toString == templateParam =>
          acc += field.toString
          true
        case _ => false
      }

    case _ => false
  }
}
