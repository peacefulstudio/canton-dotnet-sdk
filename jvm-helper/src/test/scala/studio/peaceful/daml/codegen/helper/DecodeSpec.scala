// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.{
  IntermediateDar,
  IntermediatePackage,
}

import org.scalatest.{EitherValues, Inspectors, OptionValues}
import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

import java.io.{ByteArrayOutputStream, IOException, OutputStream}
import java.nio.file.{Files, Path, Paths}
import java.util.zip.{ZipEntry, ZipFile, ZipOutputStream}

class DecodeSpec extends AnyWordSpec with Matchers with EitherValues with OptionValues with Inspectors {

  private val FixtureDar = Paths.get(
    sys.props.getOrElse("jvmHelper.testFixtureDar",
      "../tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-api-token-holding-v1/splice-api-token-holding-v1.dar"
    )
  )

  private val TemplatesFixtureDar = Paths.get(
    sys.props.getOrElse("jvmHelper.testTemplatesFixtureDar",
      "../tests/Daml.Codegen.CSharp.Tests/Snapshots/splice-amulet-name-service/splice-amulet-name-service.dar"
    )
  )


  private def rewriteDar(source: Path)(transform: (String, Array[Byte]) => Option[(String, Array[Byte])]): Path = {
    val target = Files.createTempFile("hostile-dar-", ".dar")
    val zip    = new ZipFile(source.toFile)
    val out    = new ZipOutputStream(Files.newOutputStream(target))
    try {
      val entries = zip.entries()
      while (entries.hasMoreElements) {
        val entry = entries.nextElement()
        val bytes = zip.getInputStream(entry).readAllBytes()
        transform(entry.getName, bytes).foreach { case (name, content) =>
          out.putNextEntry(new ZipEntry(name))
          out.write(content)
          out.closeEntry()
        }
      }
    } finally {
      out.close()
      zip.close()
    }
    target
  }

  private def captureStderr(body: => Int): (Int, String) = {
    val original = System.err
    val captured = new ByteArrayOutputStream()
    val replacement = new java.io.PrintStream(captured, true, "UTF-8")
    System.setErr(replacement)
    try {
      val code = body
      replacement.flush()
      (code, captured.toString("UTF-8"))
    } finally System.setErr(original)
  }

  private def runCliOnHostileDar(dar: Path, extraArgs: String*): (Int, String) = {
    val outPath = Files.createTempFile("intermediate-dar-hostile-", ".binpb")
    try {
      captureStderr(Decode.runCli(List("--dar", dar.toString, "--out", outPath.toString) ++ extraArgs))
    } finally {
      Files.deleteIfExists(outPath)
      Files.deleteIfExists(dar)
    }
  }

  private def assertReportedWithoutStackTrace(result: (Int, String)): Unit = {
    val (code, stderr) = result
    code shouldBe 1
    stderr should startWith("Failed to read DAR")
    stderr should not include "\tat "
    stderr should not include "Exception in thread"
  }

  private def nonEmptyProto: IntermediateDar =
    IntermediateDar(main = Some(IntermediatePackage(packageId = "abc", packageName = "p")))

  "Decode" should {

    "parse --dar and --out arguments" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb")) match {
        case Right(Decode.CliCommand.Run(args)) =>
          args.darPath.toString shouldBe "a.dar"
          args.outPath.toString  shouldBe "b.binpb"
          args.schemaOnly        shouldBe false
        case other => fail(s"expected Run, got $other")
      }
    }

    "default schemaOnly to false (full-decode is the default decode mode)" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb")) match {
        case Right(Decode.CliCommand.Run(args)) => args.schemaOnly shouldBe false
        case other => fail(s"expected Run, got $other")
      }
    }

    "parse --schema-only as an opt-out into schema-mode decode" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out", "b.binpb", "--schema-only")) match {
        case Right(Decode.CliCommand.Run(args)) => args.schemaOnly shouldBe true
        case other => fail(s"expected Run, got $other")
      }
    }

    "reject missing --dar" in {
      Decode.parseArgs(List("--out", "b.binpb")) shouldBe Left("Missing required argument: --dar")
    }

    "reject missing --out" in {
      Decode.parseArgs(List("--dar", "a.dar")) shouldBe Left("Missing required argument: --out")
    }

    "report a trailing --dar as a missing value, not an unrecognised argument" in {
      Decode.parseArgs(List("--out", "b.binpb", "--dar")) shouldBe Left("Missing value for --dar")
    }

    "report a trailing --out as a missing value, not an unrecognised argument" in {
      Decode.parseArgs(List("--dar", "a.dar", "--out")) shouldBe Left("Missing value for --out")
    }

    "exit with code 2 and name the missing value when --dar is the last argument" in {
      val (code, stderr) = captureStderr(Decode.runCli(List("--out", "b.binpb", "--dar")))
      code shouldBe 2
      stderr should include("Missing value for --dar")
      stderr should not include "Unrecognised argument"
    }

    "reject unknown arguments" in {
      Decode.parseArgs(List("--bogus")).left.toOption.value should startWith("Unrecognised argument")
    }

    "recognise --help as a distinct request, not an error" in {
      Decode.parseArgs(List("--help")) shouldBe Right(Decode.CliCommand.Help)
    }

    "exit with code 0 on --help" in {
      Decode.runCli(List("--help")) shouldBe 0
    }

    "recognise --version as a distinct request, not an error" in {
      Decode.parseArgs(List("--version")) shouldBe Right(Decode.CliCommand.Version)
    }

    "exit with code 0 and print exactly the build version on --version" in {
      val expectedVersion = Decode.readBuildVersion().value
      val out = new java.io.ByteArrayOutputStream()
      Console.withOut(out) { Decode.runCli(List("--version")) } shouldBe 0
      out.toString("UTF-8") shouldBe expectedVersion + System.lineSeparator()
      expectedVersion shouldBe expectedVersion.trim
      expectedVersion should not be empty
    }

    "exit with code 0 on --version even when mixed with other arguments" in {
      val expectedVersion = Decode.readBuildVersion().value
      val out = new java.io.ByteArrayOutputStream()
      Console.withOut(out) { Decode.runCli(List("--dar", "a.dar", "--version")) } shouldBe 0
      out.toString("UTF-8") shouldBe expectedVersion + System.lineSeparator()
    }

    "prefer --help over --version when both are present" in {
      Decode.parseArgs(List("--version", "--help")) shouldBe Right(Decode.CliCommand.Help)
      val out = new java.io.ByteArrayOutputStream()
      Console.withOut(out) { Decode.runCli(List("--version", "--help")) } shouldBe 0
      out.toString("UTF-8") should include("Usage: daml-dar-to-proto")
    }

    "mention --version in the usage text printed on --help" in {
      val out = new java.io.ByteArrayOutputStream()
      Console.withOut(out) { Decode.runCli(List("--help")) } shouldBe 0
      out.toString("UTF-8") should include("--version")
    }

    "report a Left when the version resource is absent from the classpath" in {
      val missing = "nonexistent-version-resource.txt"
      Decode.readBuildVersion(missing).left.toOption.value should include(missing)
    }

    "report a Left when the version resource is blank" in {
      val blank = "blank-build-version.txt"
      Decode.readBuildVersion(blank).left.toOption.value should include(blank)
    }

    "exit with code 2 on parse error" in {
      Decode.runCli(List("--bogus")) shouldBe 2
    }

    "write a non-empty IntermediateDar binary to disk for the fixture DAR" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, outPath)) shouldBe Right(())
        Files.exists(outPath) shouldBe true
        Files.size(outPath) should be > 0L
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        proto.main.value.packageId should not be empty
        proto.main.value.modules should not be empty
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "default decode threads real PartyAnalyses.compute output into the proto: every template's built-in Archive choice gets a genuine Static([]) observers verdict, and AnsRules resolves its signatories and its Archive controllers to Static([dso])" in {
      Files.exists(TemplatesFixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-default-static-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(TemplatesFixtureDar, outPath, schemaOnly = false)) shouldBe Right(())
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        val allTemplates = (proto.main.toSeq ++ proto.dependencies).flatMap(_.modules).flatMap(_.templates)
        allTemplates should not be empty
        forAll(allTemplates) { tmpl =>
          val archive = tmpl.choices
            .find(_.name == "Archive")
            .getOrElse(fail(s"built-in Archive choice not found on template ${tmpl.name}"))
          archive.observers.value.shape.isStatic shouldBe true
          archive.observers.value.getStatic.payloadFields shouldBe empty
        }
        val ansRules = allTemplates
          .find(_.name == "AnsRules")
          .getOrElse(fail("AnsRules template not found in the fixture DAR"))
        ansRules.signatories.value.getStatic.payloadFields shouldBe Seq("dso")
        ansRules.choices
          .find(_.name == "Archive")
          .getOrElse(fail("Archive choice not found on AnsRules"))
          .controllers
          .value
          .getStatic
          .payloadFields shouldBe Seq("dso")
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "--schema-only decode produces Dynamic party analysis on every template" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-schemaonly-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, outPath, schemaOnly = true)) shouldBe Right(())
        val proto = IntermediateDar.parseFrom(Files.readAllBytes(outPath))
        val mainModules = proto.main.value.modules
        val templates = mainModules.flatMap(_.templates)
        forAll(templates) { tmpl =>
          tmpl.signatories.value.shape.isDynamic shouldBe true
          tmpl.observers.value.shape.isDynamic shouldBe true
          forAll(tmpl.choices) { ch =>
            ch.controllers.value.shape.isDynamic shouldBe true
            ch.observers.value.shape.isDynamic shouldBe true
          }
        }
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "default and --schema-only decode produce identical non-party content on the fixture DAR" in {
      Files.exists(FixtureDar) shouldBe true
      val fullOut   = Files.createTempFile("intermediate-dar-full-", ".binpb")
      val schemaOut = Files.createTempFile("intermediate-dar-schema-", ".binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, fullOut, schemaOnly = false))   shouldBe Right(())
        Decode.run(Decode.ParsedArgs(FixtureDar, schemaOut, schemaOnly = true)) shouldBe Right(())
        val full   = IntermediateDar.parseFrom(Files.readAllBytes(fullOut))
        val schema = IntermediateDar.parseFrom(Files.readAllBytes(schemaOut))
        val fullStripped   = stripPartyAnalyses(full)
        val schemaStripped = stripPartyAnalyses(schema)
        fullStripped.toByteArray.toSeq shouldBe schemaStripped.toByteArray.toSeq
      } finally {
        Files.deleteIfExists(fullOut)
        Files.deleteIfExists(schemaOut)
      }
    }

    "treat a successful write as success even when stream.close() throws" in {
      val written = new java.io.ByteArrayOutputStream()
      val closeAfterSuccessfulWrite = new OutputStream {
        override def write(b: Int): Unit = written.write(b)
        override def write(b: Array[Byte], off: Int, len: Int): Unit = written.write(b, off, len)
        override def flush(): Unit = ()
        override def close(): Unit = throw new IOException("simulated close failure")
      }
      val proto = nonEmptyProto
      Decode.writeProtoToStream(closeAfterSuccessfulWrite, proto) shouldBe Right(())
      written.toByteArray.length shouldBe proto.serializedSize
    }

    "report a write failure even if stream.close() also throws" in {
      val failingWrite = new OutputStream {
        override def write(b: Int): Unit = throw new IOException("simulated write failure")
        override def write(b: Array[Byte], off: Int, len: Int): Unit =
          throw new IOException("simulated write failure")
        override def flush(): Unit = ()
        override def close(): Unit = throw new IOException("simulated close failure")
      }
      Decode.writeProtoToStream(failingWrite, nonEmptyProto) match {
        case Left(message) => (message: String) should include("simulated write failure")
        case Right(())     => fail("expected Left on write failure")
      }
    }

    "exit with code 0 when runCli drives a successful end-to-end DAR translation" in {
      Files.exists(FixtureDar) shouldBe true
      val outPath = Files.createTempFile("intermediate-dar-runcli-ok-", ".binpb")
      try {
        Decode.runCli(List(
          "--dar", FixtureDar.toString,
          "--out", outPath.toString,
        )) shouldBe 0
        Files.size(outPath) should be > 0L
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "exit with code 1 and a Failed to read DAR message when the DAR is random bytes" in {
      val randomBytes = new java.util.Random(42L)
      val noise       = new Array[Byte](4096)
      randomBytes.nextBytes(noise)
      def noiseDar = Files.write(Files.createTempFile("hostile-dar-", ".dar"), noise)
      assertReportedWithoutStackTrace(runCliOnHostileDar(noiseDar))
      assertReportedWithoutStackTrace(runCliOnHostileDar(noiseDar, "--schema-only"))
    }

    "exit with code 1 and a Failed to read DAR message when the DAR has no manifest" in {
      val dar = rewriteDar(FixtureDar) { (name, bytes) =>
        if (name == "META-INF/MANIFEST.MF") None else Some((name, bytes))
      }
      assertReportedWithoutStackTrace(runCliOnHostileDar(dar))
    }

    "exit with code 1 and a Failed to read DAR message when a dalf no longer matches its hash" in {
      val dar = rewriteDar(FixtureDar) { (name, bytes) =>
        if (name.endsWith(".dalf") && bytes.nonEmpty) Some((name, bytes.updated(bytes.length - 1, (bytes.last ^ 0x5a).toByte)))
        else Some((name, bytes))
      }
      assertReportedWithoutStackTrace(runCliOnHostileDar(dar))
    }

    "exit with code 1 and a Failed to read DAR message when a manifest line exceeds the line limit" in {
      val dar = rewriteDar(FixtureDar) { (name, bytes) =>
        if (name == "META-INF/MANIFEST.MF") {
          val oversized = ("Dalfs: " + "a" * 600 + "\n").getBytes("UTF-8")
          Some((name, bytes ++ oversized))
        } else Some((name, bytes))
      }
      assertReportedWithoutStackTrace(runCliOnHostileDar(dar))
    }

    def runCliOnDarWithManifestSuffix(suffix: String): (Int, String) = {
      val dar = rewriteDar(FixtureDar) { (name, bytes) =>
        if (name == "META-INF/MANIFEST.MF") Some((name, bytes ++ suffix.getBytes("UTF-8")))
        else Some((name, bytes))
      }
      runCliOnHostileDar(dar)
    }

    def paddingLine(contentBytes: Int): String = "Padding: " + "a" * (contentBytes - "Padding: ".length)

    "accept a manifest line of 511 bytes before its terminator, whichever terminator ends it" in {
      for (terminator <- List("\n", "\r\n", "\r")) {
        withClue(s"terminator ${terminator.map(c => c.toInt)}: ") {
          runCliOnDarWithManifestSuffix(paddingLine(511) + terminator)._1 shouldBe 0
        }
      }
    }

    "accept a manifest continuation line of 511 bytes" in {
      runCliOnDarWithManifestSuffix("Padding: x\n " + "a" * 510 + "\n")._1 shouldBe 0
    }

    "accept an unterminated final manifest line of 511 bytes" in {
      runCliOnDarWithManifestSuffix(paddingLine(511))._1 shouldBe 0
    }

    "count manifest line length in bytes, not characters" in {
      val padding = "Padding: " + "\u00e9" * 251
      runCliOnDarWithManifestSuffix(padding + "\n")._1 shouldBe 0
      assertReportedWithoutStackTrace(runCliOnDarWithManifestSuffix(padding + "a\n"))
    }

    "reject a manifest line of 512 bytes before its terminator, whichever terminator ends it" in {
      for (terminator <- List("\n", "\r\n", "\r")) {
        withClue(s"terminator ${terminator.map(c => c.toInt)}: ") {
          assertReportedWithoutStackTrace(runCliOnDarWithManifestSuffix(paddingLine(512) + terminator))
        }
      }
    }

    "reject a manifest continuation line of 512 bytes" in {
      assertReportedWithoutStackTrace(runCliOnDarWithManifestSuffix("Padding: x\n " + "a" * 511 + "\n"))
    }

    "reject an unterminated final manifest line of 512 bytes" in {
      assertReportedWithoutStackTrace(runCliOnDarWithManifestSuffix(paddingLine(512)))
    }

    "exit with code 1 when runCli fails to read a non-existent DAR" in {
      val missingDar = Paths.get("/nonexistent-dar-path-for-runcli-test.dar")
      val outPath    = Files.createTempFile("intermediate-dar-runcli-err-", ".binpb")
      try {
        Decode.runCli(List(
          "--dar", missingDar.toString,
          "--out", outPath.toString,
        )) shouldBe 1
      } finally {
        Files.deleteIfExists(outPath)
      }
    }

    "create the parent directory of the output path when writing the proto" in {
      val tmpRoot = Files.createTempDirectory("intermediate-dar-parentdir-")
      val nested  = tmpRoot.resolve("a").resolve("b").resolve("c").resolve("out.binpb")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, nested)) shouldBe Right(())
        Files.exists(nested.getParent) shouldBe true
        Files.size(nested) should be > 0L
      } finally {
        if (Files.exists(tmpRoot)) {
          val stream = Files.walk(tmpRoot)
          try stream.sorted(java.util.Comparator.reverseOrder()).forEach(p => Files.deleteIfExists(p))
          finally stream.close()
        }
      }
    }

    "report a Left when the output path cannot be opened for writing" in {
      val tmpDir = Files.createTempDirectory("intermediate-dar-asdir-")
      try {
        Decode.run(Decode.ParsedArgs(FixtureDar, tmpDir)) match {
          case Left(message) => (message: String) should include(tmpDir.toString)
          case Right(())     => fail("expected Left when output path is a directory, not a file")
        }
      } finally {
        Files.deleteIfExists(tmpDir)
      }
    }
  }

  private def stripPartyAnalyses(dar: IntermediateDar): IntermediateDar = {
    def stripPkg(pkg: IntermediatePackage): IntermediatePackage =
      pkg.copy(modules = pkg.modules.map { m =>
        m.copy(templates = m.templates.map { t =>
          t.copy(
            signatories = None,
            observers = None,
            choices = t.choices.map(c => c.copy(controllers = None, observers = None)),
          )
        })
      })
    dar.copy(
      main = dar.main.map(stripPkg),
      dependencies = dar.dependencies.map(stripPkg),
    )
  }
}
