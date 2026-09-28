// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the transport-failure shape — a transport status code, a message, a category and the
/// source exception — that <c>AcsSnapshotEntry&lt;T&gt;.StreamError</c>,
/// <c>ContractStreamEvent&lt;T&gt;.StreamError</c>,
/// <c>InterfaceAcsSnapshotEntry&lt;TInterface, TView&gt;.StreamError</c>,
/// <c>InterfaceStreamEvent&lt;TInterface, TView&gt;.StreamError</c> and
/// <c>ExerciseOutcome&lt;T&gt;.InfraError</c> each declare for themselves. The four stream
/// variants additionally carry the participant's structured error id, between the category and
/// the source exception; <c>ExerciseOutcome&lt;T&gt;.InfraError</c> does not, because the write
/// path spells a structured error as <c>ExerciseOutcome&lt;T&gt;.DamlError</c> instead. The two
/// families therefore agree on the parameters leading up to the source exception and on the
/// source exception itself, which is the whole shape on one side and all but one parameter on
/// the other. Every shape compared here is read out of the declaration by reflection, so
/// renaming, retyping, reordering or adding a parameter on one declaration alone fails this
/// class instead of letting the five drift apart.
/// </summary>
public class TransportFailureShapeAgreementTests
{
    private const string VacuityReason =
        "an empty shape would let every agreement assertion in this class pass without comparing anything";

    private static readonly Type SnapshotStreamError = typeof(AcsSnapshotEntry<Probe>.StreamError);
    private static readonly Type SubscriptionStreamError = typeof(ContractStreamEvent<Probe>.StreamError);
    private static readonly Type OutcomeInfraError = typeof(ExerciseOutcome<Probe>.InfraError);
    private static readonly Type InterfaceSnapshotStreamError =
        typeof(InterfaceAcsSnapshotEntry<ProbeInterface, ProbeView>.StreamError);
    private static readonly Type InterfaceSubscriptionStreamError =
        typeof(InterfaceStreamEvent<ProbeInterface, ProbeView>.StreamError);

    [Fact]
    public void TransportFailureShapeAgreement_the_snapshot_and_subscription_variants_declare_one_shape()
    {
        DeclaredShape(SnapshotStreamError).Should().Be(
            DeclaredShape(SubscriptionStreamError),
            "the snapshot and the subscription StreamError carry the same transport failure, so a parameter " +
            "renamed, retyped, reordered or added on one of them has to be mirrored on the other");
    }

    [Fact]
    public void TransportFailureShapeAgreement_the_interface_family_variants_declare_that_same_shape()
    {
        DeclaredShape(InterfaceSnapshotStreamError).Should().Be(
            DeclaredShape(SnapshotStreamError),
            "an interface subscription reports a transport failure with the same shape a template " +
            "subscription does, so a consumer handling both families reads one vocabulary");
        DeclaredShape(InterfaceSubscriptionStreamError).Should().Be(
            DeclaredShape(SubscriptionStreamError),
            "the live interface stream carries the same transport failure shape");
    }

    [Fact]
    public void TransportFailureShapeAgreement_the_stream_shape_opens_with_the_outcome_variants_shape()
    {
        DeclaredShape(SubscriptionStreamError).Should().StartWith(
            DeclaredShapeBefore(OutcomeInfraError, nameof(ExerciseOutcome<Probe>.InfraError.SourceException)),
            "a stream fault leads with the same transport failure the infrastructure-failure outcome declares " +
            "ahead of its source exception, so renaming, retyping or reordering the leading ones on either " +
            "side alone has to fail");
    }

    [Fact]
    public void TransportFailureShapeAgreement_every_stream_variant_carries_the_outcome_familys_error_id()
    {
        var errorId = $"{typeof(string)}? {nameof(ExerciseOutcome<Probe>.DamlError.ErrorId)}|";

        const string reason =
            "a stream consumer classifying a terminal fault needs the participant's error id, under the name " +
            "the structured write-path error already spells it rather than a second name for the same thing";

        DeclaredShape(SnapshotStreamError).Should().Contain(errorId, reason);
        DeclaredShape(SubscriptionStreamError).Should().Contain(errorId, reason);
        DeclaredShape(InterfaceSnapshotStreamError).Should().Contain(errorId, reason);
        DeclaredShape(InterfaceSubscriptionStreamError).Should().Contain(errorId, reason);
    }

    [Fact]
    public void TransportFailureShapeAgreement_every_variant_ends_with_the_source_exception()
    {
        var sourceException =
            $"{typeof(Exception)}? {nameof(ExerciseOutcome<Probe>.InfraError.SourceException)}|";

        const string reason =
            "a stream consumer deciding retry policy needs the transport exception the write path " +
            "already hands it, carried last on both families: the prefix comparison above stops at " +
            "the outcome variant's source exception, so nothing else pins its name or its type there";

        DeclaredShape(SnapshotStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(SubscriptionStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(InterfaceSnapshotStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(InterfaceSubscriptionStreamError).Should().EndWith(sourceException, reason);
        DeclaredShape(OutcomeInfraError).Should().EndWith(sourceException, reason);
    }

    [Fact]
    public void TransportFailureShapeAgreement_a_renamed_parameter_stops_agreeing()
    {
        var renamedDeclaration = new StatusCodeRenamed(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable").GetType();

        DeclaredShape(renamedDeclaration).Should().NotBe(
            DeclaredShape(SnapshotStreamError),
            "renaming a single parameter has to break the comparison, otherwise the agreement assertions " +
            "would hold against a one-sided edit and catch nothing");
    }

    [Fact]
    public void TransportFailureShapeAgreement_a_parameter_name_that_extends_a_pinned_one_stops_agreeing()
    {
        var extendedDeclaration = new MessageRenamedToMessageDetail(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "unavailable").GetType();

        DeclaredParameters(extendedDeclaration).Should().HaveSameCount(
            DeclaredParameters(SnapshotStreamError),
            "this control only proves a name that extends a pinned one is caught while it is as long as what " +
            "it is compared against; one parameter shorter and it passes on length, which is what it was " +
            "already once fixed for");

        DeclaredShape(extendedDeclaration).Should().NotStartWith(
            DeclaredShape(SnapshotStreamError),
            "a parameter name that merely extends the pinned one has to fall outside the agreement " +
            "comparisons, otherwise a one-sided rename would slip past them");
    }

    [Fact]
    public void TransportFailureShapeAgreement_reads_a_non_empty_shape_from_every_declaration()
    {
        DeclaredShape(SnapshotStreamError).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(SubscriptionStreamError).Should().NotBeEmpty(VacuityReason);
        DeclaredShape(OutcomeInfraError).Should().NotBeEmpty(VacuityReason);
        DeclaredShapeBefore(OutcomeInfraError, nameof(ExerciseOutcome<Probe>.InfraError.SourceException))
            .Should().NotBeEmpty(VacuityReason);
    }

    private static string DeclaredShape(Type declaration) =>
        Describe(DeclaredParameters(declaration));

    private static string DeclaredShapeBefore(Type declaration, string parameterName) =>
        Describe(DeclaredParameters(declaration).TakeWhile(parameter => parameter.Name != parameterName));

    private static IEnumerable<ParameterInfo> DeclaredParameters(Type declaration) =>
        declaration.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters();

    private static string Describe(IEnumerable<ParameterInfo> parameters)
    {
        var nullability = new NullabilityInfoContext();

        return string.Concat(parameters.Select(parameter =>
            $"{parameter.ParameterType}{NullableMarker(nullability, parameter)} {parameter.Name}|"));
    }

    private static string NullableMarker(NullabilityInfoContext nullability, ParameterInfo parameter) =>
        nullability.Create(parameter).WriteState == NullabilityState.Nullable ? "?" : string.Empty;

    private sealed record StatusCodeRenamed(TransportStatus StatusCode, string Message);

    private sealed record MessageRenamedToMessageDetail(
        TransportStatus Status,
        string MessageDetail,
        DamlErrorCategory? Category = null,
        string? ErrorId = null,
        Exception? SourceException = null);

    private sealed record Probe : ITemplate, IDamlRecord<Probe>
    {
        public static RuntimeIdentifier TemplateId { get; } = new("pkg", "M", "Probe");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static Probe FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }

    private sealed record ProbeInterface : IDamlInterface, IHasView<ProbeView>
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("pkg", "M", "ProbeInterface");
        public static string PackageId => "pkg";
        public static string PackageName => "probe";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } =
            new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
    }

    private sealed record ProbeView : IDamlRecord<ProbeView>
    {
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static ProbeView FromRecord(DamlRecord record) => new();

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context) =>
            throw new NotSupportedException();
    }
}
