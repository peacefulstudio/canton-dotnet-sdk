// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar

import java.io.{BufferedOutputStream, FileOutputStream, IOException, OutputStream}
import java.nio.charset.StandardCharsets
import java.nio.file.{Files, Path, Paths}
import scala.util.Try

/** Entry point for the JVM codegen helper.
  *
  * Reads a Daml `.dar` archive using `daml-lf-archive`, translates it via
  * [[AstToIntermediate]], and writes an `IntermediateDar` protobuf message
  * to the output path supplied on the command line. The default path is
  * full-decode + static party-expression analysis ([[FullDecoder]] +
  * [[PartyExpressionAnalyzer]] + [[PartyAnalyses]]), with
  * [[SignatureErasure]] applied after analysis so the proto carries only
  * data signatures plus the analyzer's verdicts. The `--schema-only`
  * opt-out routes through [[SchemaDecoder]] instead and emits `Dynamic`
  * everywhere — patch-version-insensitive but no typed-`actAs`.
  *
  * This object stays free of any `daml-lf-archive` Scala case-class shapes.
  * All coupling to upstream DA AST types lives in [[AstToIntermediate]],
  * [[SignatureErasure]], [[SchemaDecoder]], [[FullDecoder]],
  * [[PartyExpressionAnalyzer]], and [[PartyAnalyses]] — if DA rebases its
  * AST, the changes land in those files.
  */
object Decode {

  private val Usage: String =
    """Usage: daml-dar-to-proto --dar <path-to.dar> --out <path-to-output.binpb> [--schema-only]
      |
      |Options:
      |  --schema-only  Opt into the patch-version-insensitive schema-mode decode path.
      |                 The default is full-decode + static party-expression analysis,
      |                 which is patch-version-sensitive but enables typed-actAs codegen.
      |  --version      Print the build version and exit.""".stripMargin

  private val VersionResourceName = "daml-dar-to-proto-version.txt"

  def main(args: Array[String]): Unit = sys.exit(runCli(args.toList))

  /** Drives the CLI end-to-end and returns the process exit code.
    *
    *   - `0` — success (or `--help` / `--version`)
    *   - `1` — runtime failure (DAR read, decode, write, or missing version resource)
    *   - `2` — CLI usage error (unknown arg, missing required arg)
    *
    * Side-effecting on stdout/stderr; visible for testing so tests need not
    * subprocess the JAR.
    */
  def runCli(args: List[String]): Int =
    parseArgs(args) match {
      case Right(CliCommand.Help) =>
        println(Usage)
        0
      case Right(CliCommand.Version) =>
        readBuildVersion() match {
          case Right(version) =>
            println(version)
            0
          case Left(message) =>
            System.err.println(message)
            1
        }
      case Right(CliCommand.Run(parsed)) =>
        run(parsed) match {
          case Right(()) => 0
          case Left(message) =>
            System.err.println(message)
            1
        }
      case Left(message) =>
        System.err.println(message)
        System.err.println(Usage)
        2
    }

  /** Reads the DAR at `args.darPath`, translates it to an `IntermediateDar`,
    * and writes the binary proto to `args.outPath`. Visible for testing.
    *
    * `args.schemaOnly == false` (the default) runs full-decode +
    * [[PartyExpressionAnalyzer]] before erasing expression bodies, so
    * the emitted proto carries `Static`/`Dynamic` verdicts on each
    * template / choice. `args.schemaOnly == true` skips the analyser
    * and the proto carries `Dynamic` everywhere — patch-version-insensitive
    * but loses the typed-actAs codegen path.
    */
  def run(args: ParsedArgs): Either[String, Unit] =
    for {
      proto <- guardedDecode(args)(if (args.schemaOnly) decodeSchemaOnly(args) else decodeFull(args))
      _     <- writeProto(args.outPath, proto)
    } yield ()

  private def guardedDecode(args: ParsedArgs)(decode: => Either[String, IntermediateDar])
      : Either[String, IntermediateDar] =
    Try(decode).toEither
      .left
      .map(error => s"Failed to read DAR ${args.darPath.getFileName}: ${error.getClass.getName}: ${error.getMessage}")
      .flatMap(identity)

  private def decodeSchemaOnly(args: ParsedArgs)
      : Either[String, studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar] =
    SchemaDecoder
      .readDar(args.darPath.toFile)
      .map(signatures => AstToIntermediate.translate(signatures, PartyAnalyses.empty))

  private def decodeFull(args: ParsedArgs)
      : Either[String, studio.peaceful.daml.codegen.intermediate.intermediate_dar.IntermediateDar] =
    FullDecoder
      .readDar(args.darPath.toFile)
      .map { fullDar =>
        val analyses = PartyAnalyses.compute(fullDar)
        AstToIntermediate.translate(SignatureErasure.eraseDar(fullDar), analyses)
      }

  private def writeProto(out: Path, proto: IntermediateDar): Either[String, Unit] = {
    try {
      val parent = out.getParent
      if (parent != null) Files.createDirectories(parent)
      val stream = new BufferedOutputStream(new FileOutputStream(out.toFile))
      writeProtoToStream(stream, proto).left.map(message =>
        s"Failed to write proto to $out: $message"
      )
    } catch {
      case e: IOException => Left(s"Failed to write proto to $out: ${e.getMessage}")
    }
  }

  /** Writes `proto` to `stream` and closes the stream best-effort.
    *
    * Visible for testing. A `close()` failure on a stream whose write
    * already succeeded is logged-only — the write itself is the
    * meaningful unit of success, and a successful write that the OS
    * then fails to release a handle for is still a successful write.
    * A write failure overrides a close failure (write errors are
    * reported even if close also throws).
    */
  def writeProtoToStream(stream: OutputStream, proto: IntermediateDar): Either[String, Unit] = {
    var writeFailure: Option[String] = None
    try {
      proto.writeTo(stream)
      stream.flush()
    } catch {
      case e: IOException => writeFailure = Some(e.getMessage)
    }
    try stream.close()
    catch { case _: IOException => () }
    writeFailure match {
      case Some(message) => Left(message)
      case None          => Right(())
    }
  }

  /** Reads the build version baked into the classpath at assembly time.
    * Visible for testing; a missing, unreadable, or blank resource is a
    * `Left`, never a silent default.
    */
  def readBuildVersion(resourceName: String = VersionResourceName): Either[String, String] =
    Option(getClass.getClassLoader.getResourceAsStream(resourceName)) match {
      case None =>
        Left(s"Build version resource $resourceName is missing from the classpath")
      case Some(stream) =>
        try {
          val version = new String(stream.readAllBytes(), StandardCharsets.UTF_8).trim
          if (version.isEmpty) Left(s"Build version resource $resourceName is blank")
          else Right(version)
        } catch {
          case e: IOException =>
            Left(s"Build version resource $resourceName could not be read: ${e.getMessage}")
        } finally stream.close()
    }

  /** Parsed command-line arguments. */
  final case class ParsedArgs(darPath: Path, outPath: Path, schemaOnly: Boolean = false)

  /** Outcome of parsing CLI args: run with parsed args, print help, or print the version. */
  sealed trait CliCommand
  object CliCommand {
    final case class Run(args: ParsedArgs) extends CliCommand
    case object Help extends CliCommand
    case object Version extends CliCommand
  }

  /** Parses `--dar <path> --out <path>` (or `--help` / `--version`) from a
    * list of CLI arguments. `--help` and `--version` are distinct successful
    * outcomes, not errors, so that downstream tooling probing them sees exit
    * code 0; `--help` takes precedence when both are present.
    */
  def parseArgs(args: List[String]): Either[String, CliCommand] = {
    if (args.contains("--help")) Right(CliCommand.Help)
    else if (args.contains("--version")) Right(CliCommand.Version)
    else {
      @scala.annotation.tailrec
      def loop(
          remaining: List[String],
          dar: Option[Path],
          out: Option[Path],
          schemaOnly: Boolean,
      ): Either[String, CliCommand] = remaining match {
        case Nil =>
          (dar, out) match {
            case (Some(d), Some(o)) => Right(CliCommand.Run(ParsedArgs(d, o, schemaOnly)))
            case (None, _)          => Left("Missing required argument: --dar")
            case (_, None)          => Left("Missing required argument: --out")
          }
        case "--dar" :: value :: rest => loop(rest, Some(Paths.get(value)), out, schemaOnly)
        case "--out" :: value :: rest => loop(rest, dar, Some(Paths.get(value)), schemaOnly)
        case "--schema-only" :: rest  => loop(rest, dar, out, schemaOnly = true)
        case ("--dar" | "--out") :: Nil => Left(s"Missing value for ${remaining.head}")
        case other :: _               => Left(s"Unrecognised argument: $other")
      }
      loop(args, None, None, schemaOnly = false)
    }
  }
}
