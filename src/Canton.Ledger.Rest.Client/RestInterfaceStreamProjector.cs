// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Streams;
using Canton.Ledger.Rest.Client.Raw;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging;
using WireGetUpdatesResponse = Canton.Ledger.Rest.Client.Raw.GetUpdatesResponse;
using WireReassignment = Canton.Ledger.Rest.Client.Raw.Reassignment;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;

namespace Canton.Ledger.Rest.Client;

internal static class RestInterfaceStreamProjector
{
    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectUpdate<TInterface, TView>(
        WireGetUpdatesResponse response,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        RestProjectionCore.ProjectUpdate<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(response, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectTransactionEvents<TInterface, TView>(
        WireTransaction transaction,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        RestProjectionCore.ProjectTransactionEvents<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(transaction, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectReassignmentEvents<TInterface, TView>(
        WireReassignment reassignment,
        ILogger? logger = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        RestProjectionCore.ProjectReassignmentEvents<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(reassignment, logger);

    public static IEnumerable<InterfaceStreamEvent<TInterface, TView>> ProjectActiveContractEntry<TInterface, TView>(
        GetActiveContractsResponse response,
        ILogger? logger = null,
        LedgerOffset? snapshotOffset = null)
        where TInterface : IDamlInterface, IHasView<TView>
        where TView : IDamlRecord<TView> =>
        RestProjectionCore.ProjectActiveContractEntry<
            InterfaceArms<TInterface, TView>, InterfaceStreamEvent<TInterface, TView>, TInterface, TView>(
            response, logger, snapshotOffset);
}
