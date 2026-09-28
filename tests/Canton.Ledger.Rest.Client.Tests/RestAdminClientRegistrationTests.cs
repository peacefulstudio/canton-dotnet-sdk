// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestAdminClientRegistrationTests
{
    [Fact]
    public void AddRestLedgerClient_resolves_IAdminClient_as_the_RestAdminClient_from_a_REST_only_container()
    {
        using var provider = new ServiceCollection()
            .AddRestLedgerClient(options => options.HttpAddress = "http://ledger.example:7575")
            .BuildServiceProvider();

        provider.GetService<IAdminClient>().Should().BeOfType<RestAdminClient>();
    }
}
