// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class GeneratedTypeRegistryBindingLoaderTests
{
    private static readonly Identifier LateRecordId = new("pkg-late", "Registry.Late", "LateRecord");

    private sealed record LateRecord : IDamlType, IDamlRecord<LateRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(LateRecordId, DamlTypeKind.Template, "LatePackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static LateRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed record AmbiguousRecordOne : IDamlType, IDamlRecord<AmbiguousRecordOne>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-one", "Registry.Twin", "TwinRecord"), DamlTypeKind.Template, "TwinOne");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static AmbiguousRecordOne FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed record AmbiguousRecordTwo : IDamlType, IDamlRecord<AmbiguousRecordTwo>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-two", "Registry.Twin", "TwinRecord"), DamlTypeKind.Template, "TwinTwo");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static AmbiguousRecordTwo FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private static readonly Identifier VersionedTwoId = new("pkg-v2", "Registry.Versioned", "VersionedRecord");

    private sealed record VersionedRecordOne : IDamlType, IDamlRecord<VersionedRecordOne>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v1", "Registry.Versioned", "VersionedRecord"), DamlTypeKind.Template, "Versioned");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static VersionedRecordOne FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed record VersionedRecordZero : IDamlType, IDamlRecord<VersionedRecordZero>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v0", "Registry.Versioned", "VersionedRecord"), DamlTypeKind.Template, "Versioned");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static VersionedRecordZero FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed record VersionedRecordTwo : IDamlType, IDamlRecord<VersionedRecordTwo>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v2", "Registry.Versioned", "VersionedRecord"), DamlTypeKind.Template, "Versioned");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static VersionedRecordTwo FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();
    }

    private sealed class CountingLoader
    {
        public int Runs { get; private set; }

        public GeneratedTypeRegistry Registry { get; }

        public CountingLoader(Action<GeneratedTypeRegistry> registerLateBindings)
        {
            Registry = new GeneratedTypeRegistry(() =>
            {
                Runs++;
                registerLateBindings(Registry!);
            });
        }
    }

    private static RegistryLookup<DamlLfElementReader>.Resolved ResolvedRecord(RegistryLookup<DamlLfElementReader> lookup) =>
        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>().Subject;

    [Fact]
    public void FindRecordReader_runs_the_binding_loader_on_a_Missing_lookup_and_returns_what_it_registered()
    {
        var loader = new CountingLoader(registry => registry.ForRecord<LateRecord>());

        var lookup = loader.Registry.FindRecordReader(LateRecordId);

        ResolvedRecord(lookup).DeclaringType.Should().Be<LateRecord>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void FindRecordReader_stays_Missing_when_the_binding_loader_registers_nothing()
    {
        var loader = new CountingLoader(_ => { });

        var lookup = loader.Registry.FindRecordReader(LateRecordId);

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_second_Missing_lookup_does_not_run_the_binding_loader_again()
    {
        var loader = new CountingLoader(_ => { });

        loader.Registry.FindRecordReader(LateRecordId);
        loader.Registry.FindRecordReader(LateRecordId);

        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_Missing_key_or_choice_lookup_after_a_Missing_record_lookup_does_not_run_the_binding_loader_again()
    {
        var loader = new CountingLoader(_ => { });

        loader.Registry.FindRecordReader(LateRecordId);
        loader.Registry.FindKeyDescriptor(LateRecordId);
        loader.Registry.FindChoice(LateRecordId, new ChoiceName("Alpha"));

        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void FindKeyDescriptor_runs_the_binding_loader_on_its_first_Missing_lookup()
    {
        var loader = new CountingLoader(_ => { });

        loader.Registry.FindKeyDescriptor(LateRecordId);

        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void FindChoice_runs_the_binding_loader_on_its_first_Missing_lookup()
    {
        var loader = new CountingLoader(_ => { });

        loader.Registry.FindChoice(LateRecordId, new ChoiceName("Alpha"));

        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_Resolved_lookup_does_not_run_the_binding_loader()
    {
        var loader = new CountingLoader(_ => { });
        loader.Registry.ForRecord<LateRecord>();

        loader.Registry.FindRecordReader(LateRecordId);

        loader.Runs.Should().Be(0);
    }

    [Fact]
    public async Task A_Missing_lookup_on_another_thread_waits_for_the_running_binding_loader_and_sees_what_it_registered()
    {
        using var secondCallerArrived = new ManualResetEventSlim();
        var loader = new CountingLoader(registry =>
        {
            secondCallerArrived.Wait(TimeSpan.FromSeconds(10));
            Thread.Sleep(100);
            registry.ForRecord<LateRecord>();
        });
        var firstCaller = Task.Factory.StartNew(
            () => loader.Registry.FindRecordReader(LateRecordId),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        SpinWait.SpinUntil(() => loader.Runs == 1);

        var secondCaller = Task.Factory.StartNew(
            () =>
            {
                secondCallerArrived.Set();
                return loader.Registry.FindRecordReader(LateRecordId);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        var results = await Task.WhenAll(firstCaller, secondCaller);

        loader.Runs.Should().Be(1);
        results.Should().OnlyContain(lookup => lookup is RegistryLookup<DamlLfElementReader>.Resolved);
    }

    [Fact]
    public void A_lookup_that_falls_back_to_one_older_version_runs_the_binding_loader_and_returns_the_exact_version_it_registered()
    {
        var loader = new CountingLoader(registry => registry.ForRecord<VersionedRecordTwo>());
        loader.Registry.ForRecord<VersionedRecordOne>();

        var lookup = loader.Registry.FindRecordReader(VersionedTwoId);

        ResolvedRecord(lookup).DeclaringType.Should().Be<VersionedRecordTwo>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_lookup_that_falls_back_to_two_older_versions_runs_the_binding_loader_and_returns_the_exact_version_it_registered()
    {
        var loader = new CountingLoader(registry => registry.ForRecord<VersionedRecordTwo>());
        loader.Registry.ForRecord<VersionedRecordZero>();
        loader.Registry.ForRecord<VersionedRecordOne>();

        var lookup = loader.Registry.FindRecordReader(VersionedTwoId);

        ResolvedRecord(lookup).DeclaringType.Should().Be<VersionedRecordTwo>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_lookup_that_falls_back_to_one_older_version_and_finds_no_exact_version_after_loading_stays_Resolved_to_the_older_one()
    {
        var loader = new CountingLoader(_ => { });
        loader.Registry.ForRecord<VersionedRecordOne>();

        var first = loader.Registry.FindRecordReader(VersionedTwoId);
        var second = loader.Registry.FindRecordReader(VersionedTwoId);

        ResolvedRecord(first).DeclaringType.Should().Be<VersionedRecordOne>();
        ResolvedRecord(second).DeclaringType.Should().Be<VersionedRecordOne>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_lookup_with_an_exact_version_among_several_does_not_run_the_binding_loader()
    {
        var loader = new CountingLoader(_ => { });
        loader.Registry.ForRecord<VersionedRecordOne>();
        loader.Registry.ForRecord<VersionedRecordTwo>();

        var lookup = loader.Registry.FindRecordReader(VersionedTwoId);

        ResolvedRecord(lookup).DeclaringType.Should().Be<VersionedRecordTwo>();
        loader.Runs.Should().Be(0);
    }

    [Fact]
    public void An_Ambiguous_lookup_with_no_exact_identifier_runs_the_binding_loader_once_and_stays_Ambiguous_when_it_registers_nothing()
    {
        var loader = new CountingLoader(_ => { });
        loader.Registry.ForRecord<AmbiguousRecordOne>();
        loader.Registry.ForRecord<AmbiguousRecordTwo>();

        var lookup = loader.Registry.FindRecordReader(new Identifier("pkg-three", "Registry.Twin", "TwinRecord"));

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Ambiguous>();
        loader.Runs.Should().Be(1);
    }

    [Fact]
    public void A_fallback_Resolved_lookup_runs_the_binding_loader_before_accepting_the_fallback()
    {
        var loader = new CountingLoader(_ => { });
        loader.Registry.ForRecord<AmbiguousRecordOne>();

        var lookup = loader.Registry.FindRecordReader(new Identifier("pkg-three", "Registry.Twin", "TwinRecord"));

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>();
        loader.Runs.Should().Be(1, "no exact pkg-three binding is registered, so the loader runs before the single-version module/entity fallback is accepted");
    }


    [Fact]
    public void A_Missing_lookup_made_while_the_binding_loader_is_running_does_not_start_it_again()
    {
        var loader = new CountingLoader(registry => registry.FindRecordReader(LateRecordId));

        var lookup = loader.Registry.FindRecordReader(LateRecordId);

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
        loader.Runs.Should().Be(1);
    }
}
