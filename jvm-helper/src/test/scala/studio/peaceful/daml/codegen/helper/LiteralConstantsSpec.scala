// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.data.Ref.{DottedName, Name}
import com.digitalasset.daml.lf.data.{Numeric, Time}
import com.digitalasset.daml.lf.language.Ast

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.Constant

import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

class LiteralConstantsSpec extends AnyWordSpec with Matchers {

  private val IntType: Ast.Type = Ast.TBuiltin(Ast.BTInt64)

  private def value(name: String, body: Ast.Expr): (DottedName, Ast.Definition) =
    DottedName.assertFromString(name) -> Ast.DValue(IntType, body)

  private def dottedValue(segments: Seq[String], body: Ast.Expr): (DottedName, Ast.Definition) =
    DottedName.assertFromSegments(segments) -> Ast.DValue(IntType, body)

  private def int64(literal: Long): Ast.Expr = Ast.EBuiltinLit(Ast.BLInt64(literal))

  private def named(name: String) = Name.assertFromString(name)

  "LiteralConstants.fromDefinitions" should {

    "read each literal kind and order the result by ordinal name" in {
      val constants = LiteralConstants.fromDefinitions(
        Seq(
          value("retryLimit", int64(42)),
          value("negativeLimit", int64(-7)),
          value("greeting", Ast.EBuiltinLit(Ast.BLText("hello"))),
          value("maintenanceMode", Ast.EBuiltinCon(Ast.BCTrue)),
          value("disabled", Ast.EBuiltinCon(Ast.BCFalse)),
          value("feeRate", Ast.EBuiltinLit(Ast.BLNumeric(Numeric.assertFromString("0.0025000000")))),
          value("Zeta", int64(1)),
        )
      )

      constants shouldBe Seq(
        Constant("Zeta", Constant.Literal.Int64Value(1)),
        Constant("disabled", Constant.Literal.BoolValue(false)),
        Constant("feeRate", Constant.Literal.NumericValue("0.0025000000")),
        Constant("greeting", Constant.Literal.TextValue("hello")),
        Constant("maintenanceMode", Constant.Literal.BoolValue(true)),
        Constant("negativeLimit", Constant.Literal.Int64Value(-7)),
        Constant("retryLimit", Constant.Literal.Int64Value(42)),
      )
    }

    "read a literal the compiler wraps in a source location" in {
      val location = com.digitalasset.daml.lf.data.Ref.Location(
        com.digitalasset.daml.lf.data.Ref.PackageId.assertFromString("a" * 64),
        com.digitalasset.daml.lf.data.Ref.ModuleName.assertFromString("Acme"),
        "retryLimit",
        (3, 0),
        (3, 18),
      )

      LiteralConstants.fromDefinitions(
        Seq(value("retryLimit", Ast.ELocation(location, int64(42))))
      ) shouldBe Seq(Constant("retryLimit", Constant.Literal.Int64Value(42)))
    }

    "leave out a unit constructor, a date and a computation" in {
      val addition = Ast.EApp(Ast.EApp(Ast.EBuiltinFun(Ast.BAddInt64), int64(6)), int64(7))

      LiteralConstants.fromDefinitions(
        Seq(
          value("unitValue", Ast.EBuiltinCon(Ast.BCUnit)),
          value("someDate", Ast.EBuiltinLit(Ast.BLDate(Time.Date.assertFromDaysSinceEpoch(20000)))),
          value("computed", addition),
        )
      ) shouldBe empty
    }

    "leave out a generic value whose literal sits under a type abstraction" in {
      val generic = Ast.ETyAbs((named("a"), Ast.KStar), Ast.EBuiltinLit(Ast.BLText("generic")))

      LiteralConstants.fromDefinitions(Seq(value("phantom", generic))) shouldBe empty
    }

    "leave out a value the compiler named" in {
      LiteralConstants.fromDefinitions(
        Seq(
          value("$cshow", int64(1)),
          value("$dmfoo", int64(1)),
          value("$$cshow", int64(1)),
          value("$$maxtag_AmqXsC4FTpt9WzsgSw953R", int64(1)),
          value("cost$$", int64(1)),
          value("$", int64(1)),
          value("name$u00", int64(1)),
          value("name$u00zz", int64(1)),
          value("name$U0000004", int64(1)),
          value("name$x", int64(1)),
        )
      ) shouldBe empty
    }

    "keep a value whose user spelling the archive mangled" in {
      LiteralConstants.fromDefinitions(
        Seq(
          value("name$u0027", int64(1)),
          value("name$U0001f389", int64(1)),
          value("_hidden", int64(1)),
        )
      ).map(_.name) shouldBe Seq("_hidden", "name$U0001f389", "name$u0027")
    }

    "leave out a value whose name has more than one segment" in {
      LiteralConstants.fromDefinitions(Seq(dottedValue(Seq("Outer", "inner"), int64(1)))) shouldBe empty
    }

    "spell a Numeric at its declared scale in plain decimal notation" in {
      val constants = LiteralConstants.fromDefinitions(
        Seq(
          value("whole", Ast.EBuiltinLit(Ast.BLNumeric(Numeric.assertFromString("3.0000000000")))),
          value("tiny", Ast.EBuiltinLit(Ast.BLNumeric(Numeric.assertFromString("0.0000000000000000000000000000000000001")))),
        )
      )

      constants.map(c => c.name -> c.getNumericValue) shouldBe Seq(
        "tiny"  -> "0.0000000000000000000000000000000000001",
        "whole" -> "3.0000000000",
      )
    }
  }
}
