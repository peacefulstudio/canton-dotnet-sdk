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

public class GeneratedTypeRegistryTests
{
    private const string Prefix = "Daml.Runtime.Tests.GeneratedTypeRegistryTests+";

    private static DamlRecord ReadNothing(JsonElement json, DamlLfJsonDecodeContext context) => DamlRecord.Create();

    private sealed record ExactRecord : IDamlType, IDamlRecord<ExactRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-exact", "Registry.Exact", "ExactRecord"), DamlTypeKind.Template, "ExactPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static ExactRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    private sealed record VersionOneRecord : IDamlType, IDamlRecord<VersionOneRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v1", "Registry.Versioned", "VersionedRecord"), DamlTypeKind.Template, "VersionedPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static VersionOneRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    private sealed record VersionTwoRecord : IDamlType, IDamlRecord<VersionTwoRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-v2", "Registry.Versioned", "VersionedRecord"), DamlTypeKind.Template, "VersionedPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static VersionTwoRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    private sealed record TwinOneRecord : IDamlType, IDamlRecord<TwinOneRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-twin", "Registry.Twin", "TwinRecord"), DamlTypeKind.Template, "TwinPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TwinOneRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    private sealed record TwinTwoRecord : IDamlType, IDamlRecord<TwinTwoRecord>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-twin", "Registry.Twin", "TwinRecord"), DamlTypeKind.Template, "TwinPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static TwinTwoRecord FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    private sealed record ChoiceOwner : IDamlType, IHasChoices<ChoiceOwner>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-owner", "Registry.Owner", "ChoiceOwner"), DamlTypeKind.Template, "OwnerPackage");

        public static IReadOnlyList<IChoice> Choices { get; } = [CreateChoice<ChoiceOwner>("Alpha"), CreateChoice<ChoiceOwner>("Beta")];
    }

    private sealed record OwnerVersionOne : IDamlType, IHasChoices<OwnerVersionOne>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-owner-v1", "Registry.Owner", "VersionedOwner"), DamlTypeKind.Template, "OwnerPackage");

        public static IReadOnlyList<IChoice> Choices { get; } = [CreateChoice<OwnerVersionOne>("Alpha")];
    }

    private sealed record OwnerVersionTwo : IDamlType, IHasChoices<OwnerVersionTwo>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-owner-v2", "Registry.Owner", "VersionedOwner"), DamlTypeKind.Template, "OwnerPackage");

        public static IReadOnlyList<IChoice> Choices { get; } = [CreateChoice<OwnerVersionTwo>("Alpha"), CreateChoice<OwnerVersionTwo>("Added")];
    }

    private sealed record DuplicateOwnerOne : IDamlType, IHasChoices<DuplicateOwnerOne>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-dup-owner", "Registry.Owner", "DuplicateOwner"), DamlTypeKind.Template, "OwnerPackage");

        public static IReadOnlyList<IChoice> Choices { get; } = [CreateChoice<DuplicateOwnerOne>("Alpha")];
    }

    private sealed record DuplicateOwnerTwo : IDamlType, IHasChoices<DuplicateOwnerTwo>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-dup-owner", "Registry.Owner", "DuplicateOwner"), DamlTypeKind.Template, "OwnerPackage");

        public static IReadOnlyList<IChoice> Choices { get; } = [CreateChoice<DuplicateOwnerTwo>("Alpha"), CreateChoice<DuplicateOwnerTwo>("Added")];
    }

    private static Choice<TOwner, long, long> CreateChoice<TOwner>(string name)
        where TOwner : IDamlType => new()
        {
            Name = new ChoiceName(name),
            Consuming = false,
            ArgumentEncoder = value => new DamlInt64(value),
            ArgumentDecoder = value => value.As<DamlInt64>().Value,
            ResultDecoder = value => value.As<DamlInt64>().Value,
            ArgumentJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
            ResultJsonReader = (json, context) => DamlLfJsonDecoders.ReadInt64(json, context),
        };

    private static RegistryLookup<TValue>.Resolved AssertResolved<TValue>(RegistryLookup<TValue> lookup) =>
        lookup.Should().BeOfType<RegistryLookup<TValue>.Resolved>().Subject;

    private static RegistryLookup<TValue>.Ambiguous AssertAmbiguous<TValue>(RegistryLookup<TValue> lookup) =>
        lookup.Should().BeOfType<RegistryLookup<TValue>.Ambiguous>().Subject;

    [Fact]
    public void FindRecordReader_resolves_an_exact_identifier_to_its_reader_and_declaring_type()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<ExactRecord>();

        var lookup = registry.FindRecordReader(new Identifier("pkg-exact", "Registry.Exact", "ExactRecord"));

        var resolved = AssertResolved(lookup);
        resolved.Value.Should().Be((DamlLfElementReader)ExactRecord.__ReadDamlLfJson);
        resolved.DeclaringType.Should().Be<ExactRecord>();
    }

    [Fact]
    public void FindRecordReader_resolves_through_module_and_entity_when_one_declaring_type_carries_the_pair()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<ExactRecord>();

        var lookup = registry.FindRecordReader(new Identifier("pkg-other", "Registry.Exact", "ExactRecord"));

        AssertResolved(lookup).DeclaringType.Should().Be<ExactRecord>();
    }

    [Fact]
    public void FindRecordReader_is_ambiguous_through_module_and_entity_when_two_declaring_types_carry_the_pair()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<VersionTwoRecord>();
        registry.ForRecord<VersionOneRecord>();

        var lookup = registry.FindRecordReader(new Identifier("pkg-v3", "Registry.Versioned", "VersionedRecord"));

        AssertAmbiguous(lookup).Candidates.Should().Equal(Prefix + "VersionOneRecord", Prefix + "VersionTwoRecord");
    }

    [Fact]
    public void FindRecordReader_still_resolves_each_version_by_its_own_exact_identifier()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<VersionOneRecord>();
        registry.ForRecord<VersionTwoRecord>();

        var first = registry.FindRecordReader(new Identifier("pkg-v1", "Registry.Versioned", "VersionedRecord"));
        var second = registry.FindRecordReader(new Identifier("pkg-v2", "Registry.Versioned", "VersionedRecord"));

        AssertResolved(first).DeclaringType.Should().Be<VersionOneRecord>();
        AssertResolved(second).DeclaringType.Should().Be<VersionTwoRecord>();
    }

    [Fact]
    public void FindRecordReader_is_ambiguous_when_two_declaring_types_register_the_same_exact_identifier()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<TwinTwoRecord>();
        registry.ForRecord<TwinOneRecord>();

        var lookup = registry.FindRecordReader(new Identifier("pkg-twin", "Registry.Twin", "TwinRecord"));

        AssertAmbiguous(lookup).Candidates.Should().Equal(Prefix + "TwinOneRecord", Prefix + "TwinTwoRecord");
    }

    [Fact]
    public void FindRecordReader_stays_resolved_when_the_same_declaring_type_is_registered_twice()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<ExactRecord>();
        registry.ForRecord<ExactRecord>();

        var exact = registry.FindRecordReader(new Identifier("pkg-exact", "Registry.Exact", "ExactRecord"));
        var fallback = registry.FindRecordReader(new Identifier("pkg-other", "Registry.Exact", "ExactRecord"));

        AssertResolved(exact).DeclaringType.Should().Be<ExactRecord>();
        AssertResolved(fallback).DeclaringType.Should().Be<ExactRecord>();
    }

    [Fact]
    public void FindRecordReader_is_missing_for_an_identifier_nobody_registered()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForRecord<ExactRecord>();

        var lookup = registry.FindRecordReader(new Identifier("pkg-exact", "Registry.Exact", "NeverRegistered"));

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
    }

    [Fact]
    public void FindRecordReader_on_an_empty_registry_is_missing()
    {
        var lookup = new GeneratedTypeRegistry().FindRecordReader(new Identifier("pkg-exact", "Registry.Exact", "ExactRecord"));

        lookup.Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
    }

    [Fact]
    public void FindChoice_resolves_each_choice_of_a_registered_owner_by_name()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<ChoiceOwner>();
        var owner = new Identifier("pkg-owner", "Registry.Owner", "ChoiceOwner");

        var alpha = AssertResolved(registry.FindChoice(owner, new ChoiceName("Alpha")));
        var beta = AssertResolved(registry.FindChoice(owner, new ChoiceName("Beta")));

        alpha.Value.Name.Should().Be(new ChoiceName("Alpha"));
        alpha.DeclaringType.Should().Be<ChoiceOwner>();
        beta.Value.Name.Should().Be(new ChoiceName("Beta"));
    }

    [Fact]
    public void FindChoice_is_missing_for_a_choice_name_absent_on_the_resolved_owner()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<ChoiceOwner>();

        var exact = registry.FindChoice(new Identifier("pkg-owner", "Registry.Owner", "ChoiceOwner"), new ChoiceName("Gamma"));
        var fallback = registry.FindChoice(new Identifier("pkg-elsewhere", "Registry.Owner", "ChoiceOwner"), new ChoiceName("Gamma"));

        exact.Should().BeOfType<RegistryLookup<IChoice>.Missing>();
        fallback.Should().BeOfType<RegistryLookup<IChoice>.Missing>();
    }

    [Fact]
    public void FindChoice_is_ambiguous_through_module_and_entity_even_for_a_choice_only_one_version_declares()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<OwnerVersionOne>();
        registry.ForChoices<OwnerVersionTwo>();

        var lookup = registry.FindChoice(new Identifier("pkg-owner-v3", "Registry.Owner", "VersionedOwner"), new ChoiceName("Added"));

        AssertAmbiguous(lookup).Candidates.Should().Equal(Prefix + "OwnerVersionOne", Prefix + "OwnerVersionTwo");
    }

    [Fact]
    public void FindChoice_resolves_each_version_by_its_exact_identifier()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<OwnerVersionOne>();
        registry.ForChoices<OwnerVersionTwo>();

        var lookup = registry.FindChoice(new Identifier("pkg-owner-v2", "Registry.Owner", "VersionedOwner"), new ChoiceName("Added"));

        AssertResolved(lookup).DeclaringType.Should().Be<OwnerVersionTwo>();
    }

    [Fact]
    public void FindChoice_is_ambiguous_when_two_declaring_types_register_the_same_exact_identifier_even_for_a_choice_only_one_declares()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<DuplicateOwnerOne>();
        registry.ForChoices<DuplicateOwnerTwo>();

        var lookup = registry.FindChoice(new Identifier("pkg-dup-owner", "Registry.Owner", "DuplicateOwner"), new ChoiceName("Added"));

        AssertAmbiguous(lookup).Candidates.Should().Equal(Prefix + "DuplicateOwnerOne", Prefix + "DuplicateOwnerTwo");
    }

    [Fact]
    public void FindChoice_stays_resolved_when_the_same_owner_registers_its_choices_twice()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<ChoiceOwner>();
        registry.ForChoices<ChoiceOwner>();

        var lookup = registry.FindChoice(new Identifier("pkg-owner", "Registry.Owner", "ChoiceOwner"), new ChoiceName("Beta"));

        AssertResolved(lookup).DeclaringType.Should().Be<ChoiceOwner>();
    }

    [Fact]
    public void FindChoice_is_missing_for_an_unregistered_owner()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForChoices<ChoiceOwner>();

        var lookup = registry.FindChoice(new Identifier("pkg-owner", "Registry.Owner", "Unknown"), new ChoiceName("Alpha"));

        lookup.Should().BeOfType<RegistryLookup<IChoice>.Missing>();
    }

    [Fact]
    public void FindKeyDescriptor_resolves_exactly_and_through_module_and_entity_then_is_missing_otherwise()
    {
        var registry = new GeneratedTypeRegistry();
        registry.ForKey<DamlLfJsonDecodersTests.KeyedFakeTemplate, string>();

        var exact = registry.FindKeyDescriptor(new Identifier("keyed-fake-pkg", "WP1.Fakes", "KeyedFakeTemplate"));
        var fallback = registry.FindKeyDescriptor(new Identifier("keyed-fake-other", "WP1.Fakes", "KeyedFakeTemplate"));
        var absent = registry.FindKeyDescriptor(new Identifier("keyed-fake-pkg", "WP1.Fakes", "Nothing"));

        AssertResolved(exact).DeclaringType.Should().Be<DamlLfJsonDecodersTests.KeyedFakeTemplate>();
        AssertResolved(exact).Value.Should().BeSameAs(DamlLfJsonDecodersTests.KeyedFakeTemplate.Key);
        AssertResolved(fallback).DeclaringType.Should().Be<DamlLfJsonDecodersTests.KeyedFakeTemplate>();
        absent.Should().BeOfType<RegistryLookup<IKeyDescriptor>.Missing>();
    }

    [Fact]
    public void Registries_are_isolated_from_one_another_and_from_the_process_wide_one()
    {
        var first = new GeneratedTypeRegistry();
        var second = new GeneratedTypeRegistry();
        first.ForRecord<TwinOneRecord>();
        first.ForRecord<TwinTwoRecord>();
        second.ForRecord<TwinOneRecord>();
        var identifier = new Identifier("pkg-twin", "Registry.Twin", "TwinRecord");

        first.FindRecordReader(identifier).Should().BeOfType<RegistryLookup<DamlLfElementReader>.Ambiguous>();
        second.FindRecordReader(identifier).Should().BeOfType<RegistryLookup<DamlLfElementReader>.Resolved>();
        GeneratedTypeReaders.FindRecordReader(identifier).Should().BeOfType<RegistryLookup<DamlLfElementReader>.Missing>();
    }

    private sealed class TagOne;

    private sealed class TagTwo;

    private sealed class TagThree;

    private sealed class TagFour;

    private sealed record ConcurrentOwner<TTag> : IDamlType, IDamlRecord<ConcurrentOwner<TTag>>
    {
        public static DamlTypeDescriptor DamlTypeId { get; } = new(
            new Identifier("pkg-concurrent", "Registry.Concurrent", "ConcurrentRecord"), DamlTypeKind.Template, "ConcurrentPackage");

        public DamlRecord ToRecord() => DamlRecord.Create();

        public static ConcurrentOwner<TTag> FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) => ReadNothing(json, context);
    }

    [Fact]
    public async Task Registrations_racing_each_other_and_lookups_all_land_and_the_pair_ends_ambiguous()
    {
        var identifier = new Identifier("pkg-concurrent", "Registry.Concurrent", "ConcurrentRecord");
        for (var round = 0; round < 300; round++)
        {
            var registry = new GeneratedTypeRegistry();
            using var start = new ManualResetEventSlim();
            var registrations = new Action[]
            {
                registry.ForRecord<ConcurrentOwner<TagOne>>,
                registry.ForRecord<ConcurrentOwner<TagTwo>>,
                registry.ForRecord<ConcurrentOwner<TagThree>>,
                registry.ForRecord<ConcurrentOwner<TagFour>>,
            };
            var registrationTasks = registrations
                .Select(register => Task.Run(() => { start.Wait(); register(); }))
                .ToArray();
            var lookupTask = Task.Run(() =>
            {
                start.Wait();
                var verdicts = new List<RegistryLookup<DamlLfElementReader>>();
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    verdicts.Add(registry.FindRecordReader(identifier));
                }

                return verdicts;
            });
            start.Set();

            await Task.WhenAll(registrationTasks);
            var duringRegistration = await lookupTask;

            AssertAmbiguous(registry.FindRecordReader(identifier)).Candidates.Count.Should().Be(4);
            foreach (var verdict in duringRegistration.OfType<RegistryLookup<DamlLfElementReader>.Ambiguous>())
            {
                verdict.Candidates.Should().BeInAscendingOrder(StringComparer.Ordinal);
                verdict.Candidates.Count.Should().BeInRange(2, 4);
            }
        }
    }

    [Fact]
    public async Task A_lookup_started_after_every_registration_completed_never_resolves_an_ambiguous_pair()
    {
        var registry = new GeneratedTypeRegistry();
        var identifier = new Identifier("pkg-concurrent", "Registry.Concurrent", "ConcurrentRecord");
        await Task.WhenAll(
            Task.Run(registry.ForRecord<ConcurrentOwner<TagOne>>, TestContext.Current.CancellationToken),
            Task.Run(registry.ForRecord<ConcurrentOwner<TagTwo>>, TestContext.Current.CancellationToken));

        var verdicts = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(_ => Task.Run(() => registry.FindRecordReader(identifier))));

        verdicts.Should().AllBeOfType<RegistryLookup<DamlLfElementReader>.Ambiguous>();
    }

    [Fact]
    public async Task A_lookup_that_sees_one_choice_of_an_owner_sees_all_of_them()
    {
        var owner = new Identifier("pkg-owner", "Registry.Owner", "ChoiceOwner");
        for (var round = 0; round < 300; round++)
        {
            var registry = new GeneratedTypeRegistry();
            using var start = new ManualResetEventSlim();
            var registration = Task.Run(() => { start.Wait(); registry.ForChoices<ChoiceOwner>(); }, TestContext.Current.CancellationToken);
            var observation = Task.Run(() =>
            {
                start.Wait();
                var halfRegistered = false;
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var alpha = registry.FindChoice(owner, new ChoiceName("Alpha"));
                    var beta = registry.FindChoice(owner, new ChoiceName("Beta"));
                    halfRegistered |= alpha is RegistryLookup<IChoice>.Resolved && beta is RegistryLookup<IChoice>.Missing;
                }

                return halfRegistered;
            });
            start.Set();

            await registration;

            (await observation).Should().BeFalse();
        }
    }
}
