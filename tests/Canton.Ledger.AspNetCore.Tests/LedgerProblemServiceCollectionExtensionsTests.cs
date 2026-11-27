// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public class LedgerProblemServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCantonLedgerProblemDetails_gives_the_exception_handler_pipeline_and_the_middleware_one_handler_instance()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddCantonLedgerProblemDetails()
            .BuildServiceProvider();

        var forMiddleware = services.GetRequiredService<LedgerOperationExceptionHandler>();
        var forPipeline = services.GetServices<IExceptionHandler>().Should().ContainSingle().Subject;

        forPipeline.Should().BeSameAs(forMiddleware);
    }

    [Fact]
    public void AddCantonLedgerProblemDetails_called_twice_registers_one_exception_handler()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddCantonLedgerProblemDetails()
            .AddCantonLedgerProblemDetails()
            .BuildServiceProvider();

        services.GetServices<IExceptionHandler>().Should().ContainSingle();
    }
}
