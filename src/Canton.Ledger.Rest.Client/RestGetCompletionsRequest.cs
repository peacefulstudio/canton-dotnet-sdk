// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace Canton.Ledger.Rest.Client;

internal sealed record RestGetCompletionsRequest(
    [property: JsonPropertyName("parties")] IReadOnlyList<string> Parties,
    [property: JsonPropertyName("beginExclusive")] string BeginExclusive);
