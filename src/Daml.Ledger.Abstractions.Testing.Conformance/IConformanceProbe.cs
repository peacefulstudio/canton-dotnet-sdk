// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Ledger.Abstractions.Testing.Conformance;

/// <summary>
/// The Daml interface the conformance kit reads the probe contract through. A probe template
/// implements it (<c>IImplements&lt;IConformanceProbe&gt;</c>), and the seeded client serves each
/// probe contract on the interface reads under the same contract id and offset, with a
/// <see cref="ConformanceProbeView"/> as its participant-computed view.
/// </summary>
/// <remarks>
/// Shaped like an emitted interface marker, so a transport's marker lookups run against the real
/// emitted shape. A wire-level double builds the interface's wire identity from
/// <see cref="InterfaceId"/>. The identity is that of the DAR <see cref="ConformanceKitDar.Open"/>
/// returns, so a live participant that has the DAR uploaded serves the same interface.
/// </remarks>
public interface IConformanceProbe : IDamlInterface, IHasView<ConformanceProbeView>
{
    static Identifier IDamlInterface.InterfaceId => InterfaceId;

    /// <summary>The interface identity wire events must implement to classify as this interface.</summary>
    public static new Identifier InterfaceId { get; } =
        new("40acda968038dce8336b3272860ef8687a1e925349b5d89a662ceac0a77e72d4", "Conformance.Kit", "ConformanceProbe");

    static string IDamlInterface.PackageId => "40acda968038dce8336b3272860ef8687a1e925349b5d89a662ceac0a77e72d4";

    static string IDamlInterface.PackageName => "conformancekit-hddd04e2d0e6f";

    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(InterfaceId, DamlTypeKind.Interface, "conformancekit-hddd04e2d0e6f");

    /// <summary>The witness that passes this interface and its view to the interface reads.</summary>
    public static ViewDescriptor<IConformanceProbe, ConformanceProbeView> View { get; } = new();
}
