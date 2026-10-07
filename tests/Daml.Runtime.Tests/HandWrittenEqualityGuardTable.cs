// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;

namespace Daml.Runtime.Tests;

internal sealed record HandWrittenEqualityGuardEntry(
    object Sample,
    IReadOnlyDictionary<string, object?> Alternates,
    IReadOnlyDictionary<string, string> NamedExclusions);

internal static class HandWrittenEqualityGuardTable
{
    private const string KeyHashReason =
        "the ledger's hash of a key, not the key: a key read off the wire equals the same key built by a caller";

    private static readonly IReadOnlyDictionary<string, string> NoExclusions = new Dictionary<string, string>();

    private static readonly Exception SourceException = new InvalidOperationException("connection reset by peer");

    private static readonly Exception OtherSourceException = new TimeoutException("deadline exceeded");

    private static readonly TransportStatus Status = new TransportStatus.Grpc(GrpcStatusCode.Unavailable);

    private static readonly TransportStatus OtherStatus = new TransportStatus.Http(HttpStatusCode.ServiceUnavailable);

    private static readonly Identifier SampleIdentifier = new("sample-pkg", "Sample.Module", "Sample");

    private static readonly Identifier OtherIdentifier = new("other-pkg", "Other.Module", "Other");

    public static IReadOnlyDictionary<Type, HandWrittenEqualityGuardEntry> Entries { get; } = Build();

    public static IReadOnlyDictionary<Type, string> NotGuarded { get; } = new Dictionary<Type, string>
    {
        [typeof(EquatableArray<>)] = "a collection: equality is by element, with no named members",
        [typeof(NonEmpty<>)] = "a collection: equality is by element, with no named members",
        [typeof(Set<>)] = "a collection: equality is by element, with no named members",
        [typeof(Map<,>)] = "a collection: equality is by entry, with no named members",
        [typeof(DamlList)] = "a collection: equality is by element, with no named members",
        [typeof(DamlTextMap)] = "a collection: equality is by entry, with no named members",
        [typeof(DamlGenMap)] = "a collection: equality is by entry, with no named members",
        [typeof(DamlRecord)] = "a collection of fields: equality is by field, with no named members",
        [typeof(DamlNumeric)] = "a number: equality is by normalised value, so the Scale hint is excluded by design",
        [typeof(SubmitterInfo)] = "a value type over two party sets, compared by membership",
        [typeof(CommandsSubmission)] = "a submission over a command list, compared by content",
        [typeof(Formula<>.Conjunction)] = "a generated stdlib arm over a list, compared by element",
        [typeof(Formula<>.Disjunction)] = "a generated stdlib arm over a list, compared by element",
    };

    private static Dictionary<Type, HandWrittenEqualityGuardEntry> Build() =>
        new()
        {
            [typeof(ExerciseOutcome<string>.InfraError)] = new(
                new ExerciseOutcome<string>.InfraError(Status, "node unreachable", DamlErrorCategory.TransientServerFailure, SourceException),
                Alternates(
                    ("Status", OtherStatus),
                    ("Message", "node restarted"),
                    ("Category", DamlErrorCategory.ContentionOnSharedResources),
                    ("SourceException", OtherSourceException)),
                NoExclusions),
            [typeof(ExerciseOutcome<string>.CommittedUndecodable)] = new(
                new ExerciseOutcome<string>.CommittedUndecodable("update-1", "result did not decode", SourceException),
                Alternates(
                    ("UpdateId", "update-2"),
                    ("Message", "result was truncated"),
                    ("SourceException", OtherSourceException)),
                NoExclusions),
            [typeof(ExerciseOutcome<string>.DamlError)] = new(
                new ExerciseOutcome<string>.DamlError(
                    DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                    "CONTRACT_NOT_FOUND",
                    "contract 00aa is gone",
                    new Dictionary<string, string> { ["contract"] = "00aa" }),
                Alternates(
                    ("Category", DamlErrorCategory.ContentionOnSharedResources),
                    ("ErrorId", "LOCAL_VERDICT_FAILED"),
                    ("Message", "contract 00bb is gone"),
                    ("Metadata", new Dictionary<string, string> { ["contract"] = "00bb" })),
                NoExclusions),
            [typeof(ContractStreamEvent<SampleTemplate>.StreamError)] = new(
                new ContractStreamEvent<SampleTemplate>.StreamError(Status, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SourceException),
                StreamErrorAlternates(),
                NoExclusions),
            [typeof(AcsSnapshotEntry<SampleTemplate>.StreamError)] = new(
                new AcsSnapshotEntry<SampleTemplate>.StreamError(Status, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SourceException),
                StreamErrorAlternates(),
                NoExclusions),
            [typeof(InterfaceStreamEvent<ISampleInterface, SampleView>.StreamError)] = new(
                new InterfaceStreamEvent<ISampleInterface, SampleView>.StreamError(Status, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SourceException),
                StreamErrorAlternates(),
                NoExclusions),
            [typeof(InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.StreamError)] = new(
                new InterfaceAcsSnapshotEntry<ISampleInterface, SampleView>.StreamError(Status, "stream broke", DamlErrorCategory.TransientServerFailure, "STREAM_BROKE", SourceException),
                StreamErrorAlternates(),
                NoExclusions),
            [typeof(ContractKey)] = new(
                new ContractKey(new DamlText("asset-1"), SampleIdentifier) { KeyHash = "hash-1" },
                Alternates(
                    ("Value", new DamlText("asset-2")),
                    ("TemplateId", OtherIdentifier),
                    ("KeyHash", "hash-2")),
                new Dictionary<string, string> { ["KeyHash"] = KeyHashReason }),
            [typeof(DisclosedContract)] = new(
                new DisclosedContract("00c0ffee", SampleIdentifier, new byte[] { 1, 2, 3 })
                {
                    SynchronizerId = new SynchronizerId("global::1220dd"),
                },
                Alternates(
                    ("ContractId", "00c0ffef"),
                    ("TemplateId", OtherIdentifier),
                    ("CreatedEventBlob", new ReadOnlyMemory<byte>(new byte[] { 4, 5, 6 })),
                    ("SynchronizerId", new SynchronizerId("private::1220ee"))),
                NoExclusions),
        };

    private static IReadOnlyDictionary<string, object?> StreamErrorAlternates() =>
        Alternates(
            ("Status", OtherStatus),
            ("Message", "stream restarted"),
            ("Category", DamlErrorCategory.ContentionOnSharedResources),
            ("ErrorId", "STREAM_RESTARTED"),
            ("SourceException", OtherSourceException));

    private static IReadOnlyDictionary<string, object?> Alternates(params (string Member, object? Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Member, pair => pair.Value);
}
