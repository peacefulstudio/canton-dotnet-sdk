// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Localnet;

internal readonly record struct RightsGateState(
    int OpenStreams,
    bool Changing,
    int WaitingChanges,
    int WaitingStreams);
