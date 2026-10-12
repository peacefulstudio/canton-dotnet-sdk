// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0
addSbtPlugin("com.thesamet" % "sbt-protoc" % "1.0.8")

libraryDependencies += "com.thesamet.scalapb" %% "compilerplugin" % "0.11.21"

addSbtPlugin("com.eed3si9n" % "sbt-assembly" % "2.5.0")

addSbtPlugin("org.scoverage" % "sbt-scoverage" % "2.4.4")
