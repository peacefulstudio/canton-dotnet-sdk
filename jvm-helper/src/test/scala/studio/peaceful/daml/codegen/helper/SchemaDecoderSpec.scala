// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import com.digitalasset.daml.lf.archive.{DarReader => LfDarReader}

import org.scalatest.{EitherValues, OptionValues}
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.io.File
import java.nio.file.Paths

class SchemaDecoderSpec extends AnyWordSpec with Matchers with EitherValues with OptionValues {

  private val FixtureDar: File = Paths.get(
    sys.props.getOrElse("jvmHelper.testFixtureDar",
      SpliceSnapshotDars.path("splice-api-token-holding-v1").toString
    )
  ).toFile

  private lazy val lfDar = LfDarReader.readArchiveFromFile(FixtureDar)
    .toOption.getOrElse(fail(s"could not read fixture DAR at $FixtureDar via LfDarReader"))

  private lazy val ourDar = SchemaDecoder.readDar(FixtureDar)
    .getOrElse(fail(s"could not read fixture DAR at $FixtureDar via SchemaDecoder"))

  "SchemaDecoder.readDar" should {

    "designate as `main` the package upstream LfDarReader designates as main" in {
      ourDar.main._1 shouldBe lfDar.main.pkgId
    }

    "designate as `dependencies` exactly the package ids upstream LfDarReader designates as dependencies" in {
      ourDar.dependencies.map(_._1).toSet shouldBe lfDar.dependencies.map(_.pkgId).toSet
    }

    "preserve the package count round-trip with LfDarReader" in {
      (1 + ourDar.dependencies.size) shouldBe (1 + lfDar.dependencies.size)
    }

    "return a failure (not throw) for a missing file" in {
      val missing = new File("/tmp/this-file-does-not-exist-jvm-helper.dar")
      val result = SchemaDecoder.readDar(missing)
      result.isLeft shouldBe true
      result.left.toOption.value should include("missing")
        .or(include("No such"))
        .or(include("does not exist"))
        .or(include("Failed to read DAR"))
    }
  }
}
