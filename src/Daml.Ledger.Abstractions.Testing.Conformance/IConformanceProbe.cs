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
/// <see cref="InterfaceId"/>.
/// </remarks>
public interface IConformanceProbe : IDamlInterface, IHasView<ConformanceProbeView>
{
    static Identifier IDamlInterface.InterfaceId => InterfaceId;

    /// <summary>The interface identity wire events must implement to classify as this interface.</summary>
    public static new Identifier InterfaceId { get; } =
        new("ledger-conformance-kit-pkg", "Conformance.Kit", "ConformanceProbe");

    static string IDamlInterface.PackageId => "ledger-conformance-kit-pkg";

    static string IDamlInterface.PackageName => "ledger-conformance-kit";

    static Version IDamlInterface.PackageVersion => new(0, 1, 0);

    static DamlTypeDescriptor IDamlType.DamlTypeId =>
        new(InterfaceId, DamlTypeKind.Interface, "ledger-conformance-kit");

    /// <summary>The witness that passes this interface and its view to the interface reads.</summary>
    public static ViewDescriptor<IConformanceProbe, ConformanceProbeView> View { get; } = new();
}
