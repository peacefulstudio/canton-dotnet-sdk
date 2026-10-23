// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Codegen.CSharp;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Drift guard for the type-depth bound. The emitter carries its own copy of the
/// constant in another assembly and nothing makes the two agree, so a change to one
/// alone is what this exists to catch: the parser has to reject a too-deep type at
/// parse time, which it only reliably does while its bound is no looser than the
/// emitter's. The emitter's copies are reflected out by name rather than restated as
/// a literal, and any further copy added there is picked up automatically. This lives
/// beside the parser because the emitter's own test project deliberately does not
/// reference it.
/// </summary>
public sealed class TypeDepthBoundDriftGuardTests
{
    [Fact]
    public void PackageContextMaxTypeDepth_value_matches_emitter_copies()
    {
        var emitterBounds = EmitterTypeDepthBounds();

        emitterBounds.Should().NotBeEmpty(
            "a guard that reflects out no constant passes vacuously and would never catch a drift");

        emitterBounds.Should().OnlyContain(
            bound => bound.Value == PackageContext.MaxTypeDepth,
            "parse must stay the stage that rejects a too-deep type, so the parser's bound cannot drift past the emitter's copy of it");
    }

    private static IReadOnlyDictionary<string, int> EmitterTypeDepthBounds() =>
        typeof(CodegenException).Assembly
            .GetTypes()
            .Select(type => type.GetField(
                nameof(PackageContext.MaxTypeDepth),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true })
            .ToDictionary(field => field!.DeclaringType!.Name, field => (int)field!.GetRawConstantValue()!);
}
