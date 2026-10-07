// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Com.Daml.Ledger.Api.V2;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;

namespace Canton.Ledger.Grpc.Client;

internal static class GrpcInterfaceStreamProjector
{
    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectUpdate<TInterface, TView>(
        GetUpdatesResponse response,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        GrpcProjectionCore.ProjectUpdate<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(response, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectTransactionEvents<TInterface, TView>(
        Transaction transaction,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        GrpcProjectionCore.ProjectTransactionEvents<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(transaction, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectReassignmentEvents<TInterface, TView>(
        Reassignment reassignment,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        GrpcProjectionCore.ProjectReassignmentEvents<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(reassignment, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectActiveContractEntry<TInterface, TView>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        GrpcProjectionCore.ProjectActiveContractEntry<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(
            response, logger, snapshotOffset);
}
