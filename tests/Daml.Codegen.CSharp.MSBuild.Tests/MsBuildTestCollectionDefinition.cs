// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[CollectionDefinition("MSBuild")]
public sealed class MsBuildTestCollectionDefinition : ICollectionFixture<MsBuildIntegrationFixture>;
