// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;
using WireArchivedEvent = Canton.Ledger.Rest.Client.Raw.ArchivedEvent;
using WireCreatedEvent = Canton.Ledger.Rest.Client.Raw.CreatedEvent;
using WireExercisedEvent = Canton.Ledger.Rest.Client.Raw.ExercisedEvent;
using WireIdentifier = Canton.Ledger.Rest.Client.Raw.Identifier;
using WireUnassignedEvent = Canton.Ledger.Rest.Client.Raw.UnassignedEvent;

namespace Canton.Ledger.Rest.Client;

internal static class RestMarkerMatcher<TMarker>
    where TMarker : IDamlType
{
    /// <summary>
    /// The reassignment filter the JSON Ledger API accepts already scopes an interface marker's
    /// <c>Unassigned</c> events server-side — mirrors the gRPC transport's identically-named
    /// constant in <c>GrpcMarkerMatcher</c>.
    /// </summary>
    private const bool InterfaceFilterAlreadyScopesUnassignedEventsServerSide = true;

    private const int GoogleRpcCodeOk = 0;

    private static readonly DamlTypeDescriptor Marker = MarkerDescriptors.Of<TMarker>();

    public static bool IsInterface { get; } = Marker.Kind == DamlTypeKind.Interface;

    private static readonly RuntimeIdentifier MarkerIdentity = Marker.Identifier;

    /// <summary>
    /// The wire <see cref="WireIdentifier"/> used to scope a <c>TemplateFilter</c> or
    /// <c>InterfaceFilter</c> to this marker, using the package-name reference format
    /// (<c>#&lt;package-name&gt;</c>) the JSON Ledger API accepts in place of a package id.
    /// </summary>
    public static WireIdentifier FilterIdentifier => RestWireConversions.ToPackageNameReference(Marker);

    public static bool MatchesCreated(WireCreatedEvent created) =>
        IsInterface
            ? MatchesAnyInterface(created.InterfaceViews?.Select(view => view?.InterfaceId))
            : MatchesTemplate(created.PackageName, created.TemplateId);

    /// <summary>
    /// Reads the participant-computed view this interface marker's <c>InterfaceFilter</c> asked for
    /// off a wire created event — mirrors the gRPC transport's identically-named member on
    /// <c>GrpcMarkerMatcher</c>. Returns <see langword="false"/> when the matched view carries no
    /// <c>viewValue</c> or a <c>viewStatus</c> the participant did not report as <c>OK</c>.
    /// </summary>
    public static bool TryGetInterfaceViewRecord<TView>(WireCreatedEvent created, out DamlRecord record)
        where TView : IDamlRecord<TView>
    {
        if (!IsInterface)
        {
            throw new ProjectionRefusalException(
                $"{typeof(TMarker).FullName} is not a Daml interface marker; interface views only apply to IDamlInterface markers.");
        }

        foreach (var view in created.InterfaceViews ?? [])
        {
            if (view is null || !MatchesInterface(view.InterfaceId)) continue;

            if (view.ViewStatus?.Code != GoogleRpcCodeOk || view.ViewValue is null)
            {
                record = null!;
                return false;
            }

            record = RestValueDecoder.ToDamlRecord<TView>(view.ViewValue);
            return true;
        }

        record = null!;
        return false;
    }

    public static bool MatchesArchived(WireArchivedEvent archived) =>
        IsInterface
            ? MatchesAnyInterface(archived.ImplementedInterfaces?.Select(id => (WireIdentifier?)id))
            : MatchesTemplate(archived.PackageName, archived.TemplateId);

    public static bool MatchesExercised(WireExercisedEvent exercised) =>
        IsInterface
            ? MatchesAnyInterface(exercised.ImplementedInterfaces?.Select(id => (WireIdentifier?)id))
            : MatchesTemplate(exercised.PackageName, exercised.TemplateId);

    public static bool MatchesUnassigned(WireUnassignedEvent unassigned) =>
        (IsInterface && InterfaceFilterAlreadyScopesUnassignedEventsServerSide)
            || MatchesTemplate(unassigned.PackageName, unassigned.TemplateId);

    /// <summary>
    /// Matches a transport-neutral <see cref="CreatedContract"/> (as projected from a command
    /// submission's transaction result) against this marker, for use by
    /// <c>RestTransactionResultProjector.ProjectToContractId</c>.
    /// </summary>
    public static bool MatchesContract(CreatedContract created) =>
        IsInterface
            ? created.InterfaceIds.Any(id => MarkerMatch.Interface(Marker, id))
            : MarkerMatch.Template(Marker, created.PackageName, created.TemplateId);

    private static bool MatchesTemplate(string? packageName, WireIdentifier? templateId) =>
        templateId is not null
        && MarkerMatch.Template(Marker, packageName, templateId.ModuleName, templateId.EntityName);

    private static bool MatchesInterface(WireIdentifier? interfaceId) =>
        interfaceId is not null
        && MarkerMatch.Interface(Marker, interfaceId.PackageId, interfaceId.ModuleName, interfaceId.EntityName);

    private static bool MatchesAnyInterface(IEnumerable<WireIdentifier?>? identifiers) =>
        identifiers is not null && identifiers.Any(MatchesInterface);
}
