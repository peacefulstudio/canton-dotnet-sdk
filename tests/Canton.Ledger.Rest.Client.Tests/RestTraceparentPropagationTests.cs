// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestTraceparentPropagationTests
{
    private const string ParentTraceparent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string ParentTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [Fact]
    public async Task GetLedgerEndAsync_injects_the_parent_traceparent_header_over_a_real_socket()
    {
        using var server = new LoopbackHttpServer();

        var services = new ServiceCollection();
        services.AddRestLedgerClient(options => options.HttpAddress = server.HttpAddress);
        await using var provider = services.BuildServiceProvider();
        var reader = provider.GetRequiredService<ILedgerReader>();

        using var parent = new Activity("test-parent");
        parent.SetIdFormat(ActivityIdFormat.W3C);
        parent.SetParentId(ParentTraceparent);
        parent.Start();

        await reader.GetLedgerEndAsync(cancellationToken: TestContext.Current.CancellationToken);

        parent.Stop();

        var traceparent = await server.CapturedTraceparent
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        traceparent.Should().NotBeNull();
        traceparent!.Should().StartWith($"00-{ParentTraceId}-");
    }
}
