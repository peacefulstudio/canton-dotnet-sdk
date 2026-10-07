// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Codegen.Testing.Conformance.Tests;

internal static partial class GeneratedStjSampleTable
{
    private static readonly Party Alice = new("alice");
    private static readonly Party Bob = new("bob");

    public static IReadOnlyDictionary<Type, object[]> Samples { get; } = Build();

    public static IReadOnlyDictionary<string, string> KnownBroken { get; } = BuildKnownBroken();

    public static IReadOnlyDictionary<string, string> ArmsWithNoFullyPopulatedForm { get; } = new Dictionary<string, string>();

    private static Dictionary<Type, object[]> Build()
    {
        var samples = new Dictionary<Type, object[]>();
        AddRichTypes(samples);
        AddRichTypeChoiceArguments(samples);
        AddContractKeys(samples);
        AddSubmitShapes(samples);
        AddSingleTemplateFamilies(samples);
        return samples;
    }
}
