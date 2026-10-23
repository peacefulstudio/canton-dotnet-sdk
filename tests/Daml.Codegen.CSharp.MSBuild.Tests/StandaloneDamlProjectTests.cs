// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.MSBuild.TestSupport;
using Xunit;

namespace Daml.Codegen.CSharp.MSBuild.Tests;

[Collection("MSBuild")]
public class StandaloneDamlProjectTests : DamlProjectStepContractTests
{
    private readonly MsBuildIntegrationFixture _fx;

    public StandaloneDamlProjectTests(MsBuildIntegrationFixture fx) : base(fx)
    {
        _fx = fx;
    }

    protected override void SeedFakeTools(string toolsDirectory) => _fx.SeedFakeDpm(toolsDirectory);

    private protected override void WriteConsumerProject(string projectDirectory, IReadOnlyList<DamlProjectItem> damlProjects) =>
        ConsumerProjectWriter.Write(
            projectDirectory,
            _fx.PropsFile,
            _fx.TargetsFile,
            damlProjects: damlProjects,
            damlCodegenMode: "Standalone");
}
