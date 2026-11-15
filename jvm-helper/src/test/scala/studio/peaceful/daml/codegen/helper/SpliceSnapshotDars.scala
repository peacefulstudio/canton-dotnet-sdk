// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import java.nio.file.{Path, Paths}

import scala.sys.process.{Process, ProcessLogger}

object SpliceSnapshotDars {

  private val FetchScript = "../scripts/fetch-splice-snapshot-dars.sh"

  private lazy val directory: Path = {
    val cacheDirectory = sys.env
      .get("SPLICE_SNAPSHOT_DARS_DIR")
      .map(Paths.get(_))
      .getOrElse(Paths.get("../output/splice-snapshot-dars"))
      .toAbsolutePath
      .normalize()
    val log = new StringBuilder
    val exitCode = Process(Seq(BashExecutable.path, FetchScript, "--dir", cacheDirectory.toString.replace('\\', '/')))
      .!(ProcessLogger(line => log.append(line).append('\n'), line => log.append(line).append('\n')))
    if (exitCode != 0)
      throw new IllegalStateException(s"$FetchScript failed with exit code $exitCode:\n$log")
    cacheDirectory
  }

  def path(snapshot: String): Path = directory.resolve(s"$snapshot.dar")
}
