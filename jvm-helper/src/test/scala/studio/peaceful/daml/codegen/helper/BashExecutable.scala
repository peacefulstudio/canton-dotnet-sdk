// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import java.io.File

/** The `bash` every test that shells out launches. On Windows, `CreateProcess` resolves a bare
  * `bash` through the system directory before `PATH`, which finds the WSL launcher
  * (`System32\bash.exe`) ahead of Git Bash, so Git Bash is started by the full path found on `PATH`.
  */
object BashExecutable {

  private val WindowsAppsSegment = """\WindowsApps"""

  private val DefaultWindowsDirectory = """C:\Windows"""

  lazy val path: String =
    if (!sys.props.getOrElse("os.name", "").startsWith("Windows")) "bash"
    else
      locateOnWindowsPath(
        environmentVariable(sys.env, "PATH").getOrElse(""),
        environmentVariable(sys.env, "SystemRoot").getOrElse(DefaultWindowsDirectory),
        file => new File(file).isFile
      ).getOrElse(
        throw new IllegalStateException(
          "Git Bash was not found on PATH outside the Windows directory; install Git for Windows."
        )
      )

  def environmentVariable(environment: Map[String, String], name: String): Option[String] =
    environment.collectFirst { case (key, value) if key.equalsIgnoreCase(name) => value }

  def locateOnWindowsPath(
      pathVariable: String,
      windowsDirectory: String,
      fileExists: String => Boolean
  ): Option[String] =
    pathVariable
      .split(';')
      .iterator
      .filter(_.nonEmpty)
      .map(_.stripSuffix("\\"))
      .filterNot(isWslLauncherDirectory(_, windowsDirectory))
      .map(_ + """\bash.exe""")
      .find(fileExists)

  private def isWslLauncherDirectory(directory: String, windowsDirectory: String): Boolean =
    (directory + "\\").toLowerCase.startsWith((windowsDirectory + "\\").toLowerCase) ||
      directory.toLowerCase.contains(WindowsAppsSegment.toLowerCase)
}
