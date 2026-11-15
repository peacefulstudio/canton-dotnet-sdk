// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public class LedgerProblemMiddlewareTests
{
    private static Task<LedgerApiHost> StartWithMiddlewareAsync(
        Action<WebApplication> mapEndpoints,
        Action<IApplicationBuilder>? outerMiddleware = null,
        Action<IServiceCollection>? configureServices = null) =>
        LedgerApiHost.StartAsync(
            mapEndpoints,
            configureServices,
            usePipeline: app =>
            {
                outerMiddleware?.Invoke(app);
                app.UseCantonLedgerProblemDetails();
            });

    public static TheoryData<string, LedgerOperationException, int, string> MappedFailures => new()
    {
        {
            "contention",
            LedgerFailures.Rejected(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS"),
            409, "Conflict"
        },
        {
            "committed",
            LedgerFailures.CommittedUndecodable(updateId: "update-9", commandId: "cmd-17"),
            202, "Accepted"
        },
        {
            "unavailable",
            LedgerFailures.Rejected(DamlErrorCategory.TransientServerFailure, "SERVICE_NOT_RUNNING"),
            502, "Ledger Unavailable"
        },
    };

    [Theory]
    [MemberData(nameof(MappedFailures))]
    public async Task A_ledger_failure_thrown_from_an_endpoint_answers_the_mapped_problem(
        string failureKind, LedgerOperationException failure, int expectedStatus, string expectedTitle)
    {
        _ = failureKind;
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw failure)));

        var (response, problem) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    [Fact]
    public async Task A_committed_failure_carries_its_command_and_update_ids()
    {
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.CommittedUndecodable(
                updateId: "update-9", commandId: "cmd-17"))));

        var (_, problem) = await host.GetProblemAsync("/fail");

        problem.GetProperty("commandId").GetString().Should().Be("cmd-17");
        problem.GetProperty("updateId").GetString().Should().Be("update-9");
    }

    [Fact]
    public async Task A_ledger_failure_answers_problem_json_to_a_client_that_asks_for_html()
    {
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS")))
                .ProducesLedgerProblems());

        var (response, problem) = await host.GetProblemAsync("/fail", accept: "text/html");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("title").GetString().Should().Be("Conflict");
    }

    [Fact]
    public async Task A_ledger_failure_is_logged_as_a_warning_naming_its_operation_and_the_status_answered()
    {
        var logs = new CapturedLogs();
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS"))),
            configureServices: services => services.AddSingleton<ILoggerProvider>(logs));

        await host.GetAsync("/fail");

        logs.Entries.Should().Contain((
            LogLevel.Warning,
            "Ledger operation ConnectionRequest.Accept failed (NotCommitted, LOCAL_VERDICT_LOCKED_CONTRACTS) on GET /fail; answering 409"));
    }

    [Fact]
    public async Task A_non_ledger_exception_reaches_the_outer_middleware_as_the_same_instance_and_the_host_answers_it()
    {
        var thrown = new InvalidOperationException("not a ledger failure");
        Exception? seenByHost = null;
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw thrown)),
            outerMiddleware: app => app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (Exception exception)
                {
                    seenByHost = exception;
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync("host catch-all");
                }
            }));

        var response = await host.GetAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("host catch-all");
        seenByHost.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task A_request_logging_middleware_outside_it_observes_the_mapped_status_and_no_exception()
    {
        var observed = new List<(int Status, Exception? Exception)>();
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS"))),
            outerMiddleware: app => app.Use(async (context, next) =>
            {
                Exception? escaped = null;
                try
                {
                    await next(context);
                }
                catch (Exception exception)
                {
                    escaped = exception;
                    throw;
                }
                finally
                {
                    observed.Add((context.Response.StatusCode, escaped));
                }
            }));

        await host.GetAsync("/fail");

        observed.Should().ContainSingle().Which.Should().Be((409, (Exception?)null));
    }

    [Fact]
    public async Task A_ledger_failure_after_the_response_started_propagates_untouched_to_the_outer_middleware()
    {
        var thrown = LedgerFailures.Rejected(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS");
        Exception? seenByHost = null;
        await using var host = await StartWithMiddlewareAsync(
            app => app.MapGet("/fail", async (HttpContext context) =>
            {
                await context.Response.WriteAsync("partial");
                await context.Response.Body.FlushAsync(context.RequestAborted);
                throw thrown;
            }),
            outerMiddleware: app => app.Use(async (context, next) =>
            {
                try
                {
                    await next(context);
                }
                catch (LedgerOperationException exception)
                {
                    seenByHost = exception;
                }
            }));

        var response = await host.GetAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("partial");
        seenByHost.Should().BeSameAs(thrown);
    }

    [Fact]
    public void Using_it_without_registering_the_ledger_problem_details_fails_naming_the_registration()
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());

        var use = () => app.UseCantonLedgerProblemDetails();

        use.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddCantonLedgerProblemDetails*");
    }
}
