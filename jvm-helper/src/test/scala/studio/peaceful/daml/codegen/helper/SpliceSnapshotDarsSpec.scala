// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.nio.file.Files
import java.security.MessageDigest

class SpliceSnapshotDarsSpec extends AnyWordSpec with Matchers {

  private def sha256Hex(bytes: Array[Byte]): String =
    MessageDigest.getInstance("SHA-256").digest(bytes).map(byte => f"$byte%02x").mkString

  "SpliceSnapshotDars.path" should {

    "resolve a snapshot to the DAR whose sha256 the manifest pins" in {
      val dar = SpliceSnapshotDars.path("splice-api-token-holding-v1")

      sha256Hex(Files.readAllBytes(dar)) shouldBe "ef75f8eb41a65810221784fdb78bb9dfac7cb22245aba14fa7cb7f69c34e0175"
    }

    "resolve a snapshot of another package to its own DAR" in {
      val dar = SpliceSnapshotDars.path("splice-amulet-name-service")

      sha256Hex(Files.readAllBytes(dar)) shouldBe "986c6ccf67477246802df410fc7b94ef9fdae5f4b739e2d5ed5533e5e658aeb6"
    }
  }
}
