// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Kernel.Wire;

namespace Canton.Ledger.Rest.Client;

internal sealed record RestCall(
    HttpMethod Method,
    string Path,
    object? Body,
    string MissingBodyMessage,
    string MalformedBodyMessagePrefix,
    LedgerCallKind Kind,
    bool Replayable = true,
    string? Accept = null);
