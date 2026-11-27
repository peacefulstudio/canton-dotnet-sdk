// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public sealed class LedgerFailureCancellationTests : IDisposable
{
    private readonly CapturedLogs _logs = new();
    private readonly ServiceProvider _services;

    public LedgerFailureCancellationTests() =>
        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(_logs))
            .AddCantonLedgerProblemDetails()
            .BuildServiceProvider();

    public void Dispose() => _services.Dispose();

    private static DefaultHttpContext HtmlOnlyRequest(CancellationToken requestAborted)
    {
        var context = new DefaultHttpContext { RequestAborted = requestAborted };
        context.Request.Method = "GET";
        context.Request.Path = "/fail";
        context.Request.Headers.Accept = "text/html";
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task TryHandleAsync_with_a_cancelled_token_logs_the_failure_and_abandons_the_fallback_write()
    {
        var handler = _services.GetRequiredService<LedgerOperationExceptionHandler>();
        var context = HtmlOnlyRequest(CancellationToken.None);
        var failure = LedgerFailures.Rejected(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS");

        var handling = async () => await handler.TryHandleAsync(context, failure, new CancellationToken(canceled: true));

        await handling.Should().ThrowAsync<OperationCanceledException>();
        context.Response.Body.Length.Should().Be(0);
        _logs.Entries.Should().Contain((
            LogLevel.Warning,
            "Ledger operation ConnectionRequest.Accept failed (NotCommitted, LOCAL_VERDICT_LOCKED_CONTRACTS) on GET /fail; answering 409"));
    }

    [Fact]
    public async Task UseCantonLedgerProblemDetails_on_an_aborted_request_logs_the_failure_and_abandons_the_fallback_write()
    {
        var failure = LedgerFailures.Rejected(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS");
        var middleware = new LedgerOperationExceptionMiddleware(
            _ => throw failure, _services.GetRequiredService<LedgerOperationExceptionHandler>());
        var context = HtmlOnlyRequest(new CancellationToken(canceled: true));

        var invoking = async () => await middleware.InvokeAsync(context);

        await invoking.Should().ThrowAsync<OperationCanceledException>();
        context.Response.Body.Length.Should().Be(0);
        _logs.Entries.Should().Contain((
            LogLevel.Warning,
            "Ledger operation ConnectionRequest.Accept failed (NotCommitted, LOCAL_VERDICT_LOCKED_CONTRACTS) on GET /fail; answering 409"));
    }
}
