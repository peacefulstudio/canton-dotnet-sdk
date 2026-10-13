// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0
ThisBuild / scalaVersion := "2.13.18"
ThisBuild / organization := "studio.peaceful.daml.codegen"
ThisBuild / version      := sys.env.get("DAML_DAR_TO_PROTO_VERSION").map(_.trim).filter(_.nonEmpty).getOrElse("0.0.0-dev")

resolvers += "Daml" at "https://repo1.maven.org/maven2/"

lazy val damlLfArchiveVersion = "3.6.1"

lazy val jvmHelper = (project in file("."))
  .settings(
    name := "daml-dar-to-proto",
    Compile / resourceGenerators += Def.task {
      val versionFile = (Compile / resourceManaged).value / "daml-dar-to-proto-version.txt"
      IO.write(versionFile, version.value)
      Seq(versionFile)
    }.taskValue,
    Compile / PB.protoSources := Seq((ThisBuild / baseDirectory).value / ".." / "proto"),
    Compile / PB.targets := Seq(
      scalapb.gen() -> (Compile / sourceManaged).value / "scalapb"
    ),
    libraryDependencies ++= Seq(
      "com.daml" %% "daml-lf-archive" % damlLfArchiveVersion,
      "com.thesamet.scalapb" %% "scalapb-runtime" % scalapb.compiler.Version.scalapbVersion % "protobuf",
      "org.scalatest" %% "scalatest" % "3.2.20" % Test
    ),
    assembly / mainClass := Some("studio.peaceful.daml.codegen.helper.Decode"),
    assembly / assemblyJarName := "daml-dar-to-proto.jar",
    assembly / assemblyMergeStrategy := {
      case PathList("META-INF", "MANIFEST.MF")              => MergeStrategy.discard
      case PathList("META-INF", "versions", _, "module-info.class") => MergeStrategy.discard
      case PathList("module-info.class")                    => MergeStrategy.discard
      case PathList("META-INF", "io.netty.versions.properties") => MergeStrategy.first
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".SF"))  => MergeStrategy.discard
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".DSA")) => MergeStrategy.discard
      case PathList("META-INF", xs @ _*) if xs.lastOption.exists(_.endsWith(".RSA")) => MergeStrategy.discard
      case PathList("google", "protobuf", _*)               => MergeStrategy.first
      case PathList("scala", "annotation", "nowarn.class")  => MergeStrategy.first
      case PathList("scala", "annotation", "nowarn$.class") => MergeStrategy.first
      case x =>
        val old = (assembly / assemblyMergeStrategy).value
        old(x)
    },
    scalacOptions ++= Seq(
      "-deprecation",
      "-feature",
      "-Werror",
      "-Wunused:imports"
    ),
    coverageExcludedPackages := "studio\\.peaceful\\.daml\\.codegen\\.intermediate\\..*",
    coverageOutputCobertura := true,
    coverageOutputXML := true
  )
