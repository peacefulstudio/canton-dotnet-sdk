// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Testing.LiveConformance;
using Xunit;

namespace Canton.Ledger.Rest.Client.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class RestLedgerClientLiveConformanceTests(RestLiveConformanceFixture fixture)
    : LiveConformanceTests(fixture), IClassFixture<RestLiveConformanceFixture>;
