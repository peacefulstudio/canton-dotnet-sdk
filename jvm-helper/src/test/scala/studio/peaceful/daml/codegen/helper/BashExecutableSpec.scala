// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

package studio.peaceful.daml.codegen.helper

import org.scalatest.matchers.should.Matchers
import org.scalatest.wordspec.AnyWordSpec

class BashExecutableSpec extends AnyWordSpec with Matchers {

  private def win(path: String): String = path.replace('/', '\\')

  private val windowsDirectory = win("C:/Windows")

  "BashExecutable.locateOnWindowsPath" should {

    "skip the WSL launcher in the system directory" in {
      val located = BashExecutable.locateOnWindowsPath(
        win("C:/Windows/System32;C:/Program Files/Git/usr/bin"),
        windowsDirectory,
        _ => true
      )

      located shouldBe Some(win("C:/Program Files/Git/usr/bin/bash.exe"))
    }

    "skip the WSL launcher alias in WindowsApps" in {
      val located = BashExecutable.locateOnWindowsPath(
        win("C:/Users/runner/AppData/Local/Microsoft/WindowsApps;C:/Program Files/Git/bin"),
        windowsDirectory,
        _ => true
      )

      located shouldBe Some(win("C:/Program Files/Git/bin/bash.exe"))
    }

    "skip directories without a bash executable" in {
      val located = BashExecutable.locateOnWindowsPath(
        win("C:/tools;C:/Program Files/Git/bin"),
        windowsDirectory,
        _ == win("C:/Program Files/Git/bin/bash.exe")
      )

      located shouldBe Some(win("C:/Program Files/Git/bin/bash.exe"))
    }

    "tolerate empty entries and a trailing separator" in {
      val located = BashExecutable.locateOnWindowsPath(
        win(";C:/Program Files/Git/bin/;"),
        windowsDirectory,
        _ => true
      )

      located shouldBe Some(win("C:/Program Files/Git/bin/bash.exe"))
    }

    "compare the system directory without regard to case" in {
      val located = BashExecutable.locateOnWindowsPath(
        win("c:/windows/system32;C:/Program Files/Git/bin"),
        windowsDirectory,
        _ => true
      )

      located shouldBe Some(win("C:/Program Files/Git/bin/bash.exe"))
    }

    "find nothing when only the system launcher exists" in {
      val located = BashExecutable.locateOnWindowsPath(
        win("C:/Windows/System32"),
        windowsDirectory,
        _ => true
      )

      located shouldBe None
    }
  }

  "BashExecutable.environmentVariable" should {

    "find a variable spelled with the exact name" in {
      BashExecutable.environmentVariable(Map("PATH" -> "C:\\bin"), "PATH") shouldBe Some("C:\\bin")
    }

    "find PATH when Windows spells it Path" in {
      BashExecutable.environmentVariable(Map("Path" -> "C:\\bin"), "PATH") shouldBe Some("C:\\bin")
    }

    "find nothing when the variable is absent" in {
      BashExecutable.environmentVariable(Map("HOME" -> "C:\\Users"), "PATH") shouldBe None
    }
  }
}
