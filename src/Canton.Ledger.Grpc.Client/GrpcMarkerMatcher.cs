// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Grpc;
using Google.Rpc;
using ProtoArchivedEvent = Com.Daml.Ledger.Api.V2.ArchivedEvent;
using ProtoCreatedEvent = Com.Daml.Ledger.Api.V2.CreatedEvent;
using ProtoExercisedEvent = Com.Daml.Ledger.Api.V2.ExercisedEvent;
using ProtoIdentifier = Com.Daml.Ledger.Api.V2.Identifier;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcMarkerMatcher<TMarker>
    where TMarker : IDamlType
{
    private const bool InterfaceReassignmentFilterSelectsUnassignedEventsServerSide = true;

    private static readonly DamlTypeDescriptor MarkerDescriptor = ResolveMarkerDescriptor();

    public static bool IsInterface { get; } = MarkerDescriptor.Kind == DamlTypeKind.Interface;

    private static readonly RuntimeIdentifier MarkerIdentity = MarkerDescriptor.Identifier;

    private static readonly string PackageName = MarkerDescriptor.PackageName;

    public static bool Matches(CreatedContract created) =>
        IsInterface
            ? created.InterfaceIds.Any(id => MarkerMatch.Interface(MarkerDescriptor, id))
            : MarkerMatch.Template(MarkerDescriptor, created.PackageName, created.TemplateId);

    public static ProtoIdentifier StreamFilterIdentifier() =>
        DamlValueConverter.ToProtoTemplateNameIdentifier(PackageName, MarkerIdentity);

    public static bool MatchesProtoCreated(ProtoCreatedEvent created)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(created.InterfaceViews.Select(view => view.InterfaceId));
        }

        return MatchesTemplate(created.PackageName, created.TemplateId);
    }

    public static bool MatchesProtoArchived(ProtoArchivedEvent archived)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(archived.ImplementedInterfaces);
        }

        return MatchesTemplate(archived.PackageName, archived.TemplateId);
    }

    public static bool MatchesProtoExercised(ProtoExercisedEvent exercised)
    {
        if (IsInterface)
        {
            return MatchesAnyImplementedInterface(exercised.ImplementedInterfaces);
        }

        return MatchesTemplate(exercised.PackageName, exercised.TemplateId);
    }

    public static bool MatchesProtoUnassigned(UnassignedEvent unassigned)
    {
        if (IsInterface)
        {
            return InterfaceReassignmentFilterSelectsUnassignedEventsServerSide;
        }

        return MatchesTemplate(unassigned.PackageName, unassigned.TemplateId);
    }

    public static bool TryGetInterfaceViewRecord(ProtoCreatedEvent created, out DamlRecord record)
    {
        if (!IsInterface)
        {
            throw new InvalidOperationException(
                $"{typeof(TMarker).FullName} is not a Daml interface marker; interface views only apply to IDamlInterface markers.");
        }

        foreach (var view in created.InterfaceViews)
        {
            if (!MatchesInterface(view.InterfaceId)) continue;

            if (view.ViewStatus is null || view.ViewStatus.Code != (int)Code.Ok || view.ViewValue is null)
            {
                record = null!;
                return false;
            }

            record = DamlValueConverter.FromProtoRecord(view.ViewValue);
            return true;
        }

        record = null!;
        return false;
    }

    private static bool MatchesAnyImplementedInterface(IEnumerable<ProtoIdentifier> implementedInterfaces)
    {
        foreach (var implemented in implementedInterfaces)
        {
            if (MatchesInterface(implemented)) return true;
        }
        return false;
    }

    private static bool MatchesTemplate(string? packageName, ProtoIdentifier? templateId) =>
        templateId is not null
        && MarkerMatch.Template(MarkerDescriptor, packageName, templateId.ModuleName, templateId.EntityName);

    private static bool MatchesInterface(ProtoIdentifier? interfaceId) =>
        interfaceId is not null
        && MarkerMatch.Interface(MarkerDescriptor, interfaceId.PackageId, interfaceId.ModuleName, interfaceId.EntityName);

    private static DamlTypeDescriptor ResolveMarkerDescriptor()
    {
        if (!typeof(IDamlInterface).IsAssignableFrom(typeof(TMarker))
            && !typeof(ITemplate).IsAssignableFrom(typeof(TMarker)))
        {
            throw new InvalidOperationException(
                $"{typeof(TMarker).FullName} implements IDamlType but is neither IDamlInterface nor ITemplate.");
        }

        return TMarker.DamlTypeId;
    }
}
