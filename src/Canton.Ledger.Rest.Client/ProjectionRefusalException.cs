// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

internal sealed class ProjectionRefusalException(string message) : InvalidOperationException(message);
