// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import org.scalatest.EitherValues
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.nio.file.Paths

/** Exercises [[PartyAnalyses.compute]] against the committed
  * splice-amulet-name-service DAR as real-world ground truth for the
  * `toParties` dictionary-method indirection.
  */
class PartyAnalysesSpec extends AnyWordSpec with Matchers with EitherValues {

  private val TemplatesFixtureDar = Paths.get(
    sys.props.getOrElse(
      "jvmHelper.testTemplatesFixtureDar",
      SpliceSnapshotDars.path("splice-amulet-name-service").toString,
    )
  )

  private lazy val ansRulesAnalysis: TemplatePartyAnalysis = {
    val dar = FullDecoder.readDar(TemplatesFixtureDar.toFile).value
    val verdicts = PartyAnalyses.compute(dar).byTemplate.collect {
      case ((_, _, templateName), analysis) if templateName.dottedName == "AnsRules" => analysis
    }
    verdicts should have size 1
    verdicts.head
  }

  "PartyAnalyses.compute on the real splice-amulet-name-service DAR" should {

    "resolve AnsRules observers to Static empty through the toParties dictionary-method indirection" in {
      ansRulesAnalysis.observers shouldBe PartyAnalysisResult.Static(Nil)
    }

    "resolve AnsRules signatories to the dso payload field through the located toParties indirection" in {
      ansRulesAnalysis.signatories shouldBe PartyAnalysisResult.Static(List("dso"))
    }
  }
}
