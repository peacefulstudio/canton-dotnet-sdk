// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.MSBuild.TestSupport;

/// <summary>
/// What <see cref="DamlGenerateBindingsAdapterContractTests"/> needs from the per-adapter
/// fixture: where the adapter's <c>.props</c> and <c>.targets</c> live, which DAR to generate from,
/// and how to put a fake <c>dpm</c> on a tools directory.
/// </summary>
public interface IMsBuildAdapterFixture
{
    string PropsFile { get; }
    string TargetsFile { get; }
    string FixtureDar { get; }

    void SeedFakeDpm(string toolsDirectory);
}
