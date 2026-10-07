// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static readonly Party Alice = new("Alice::1220aa");
    private static readonly Party Bob = new("Bob::1220bb");
    private static readonly Party Carol = new("Carol::1220cc");
    private static readonly SynchronizerId GlobalSynchronizer = new("global::1220dd");
    private static readonly SynchronizerId PrivateSynchronizer = new("private::1220ee");
    private static readonly Identifier SampleIdentifier = new("sample-pkg", "Sample.Module", "Sample");
    private static readonly Identifier OtherIdentifier = new("other-pkg", "Other.Module", "Other");
    private static readonly DamlUndecodedJson SampleUndecodedJson = new("{\"amount\":\"42\"}");
    private static readonly DateTimeOffset SampleInstant = new(2026, 10, 5, 12, 30, 15, 123, TimeSpan.Zero);

    public static IReadOnlyDictionary<Type, object[]> Samples { get; } = Build();

    public static IReadOnlyDictionary<Type, string> WriteOnly { get; } = BuildWriteOnly();

    public static IReadOnlyDictionary<string, string> KnownBroken { get; } = BuildKnownBroken();

    public static IReadOnlyDictionary<string, string> ArmsWithNoFullyPopulatedForm { get; } = new Dictionary<string, string>();

    private static Exception SampleSourceException => ThrownAndCaught();

    private static Exception ThrownAndCaught()
    {
        try
        {
            throw new InvalidOperationException("connection reset by peer");
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }

    private static EquatableArray<T> Eq<T>(params ReadOnlySpan<T> items) => EquatableArray.Create(items);

    private static Dictionary<Type, object[]> Build()
    {
        var samples = new Dictionary<Type, object[]>();
        AddData(samples);
        AddCommands(samples);
        AddContracts(samples);
        AddOutcomes(samples);
        AddStdlib(samples);
        AddStreams(samples);
        return samples;
    }
}
