// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

[ApiController]
[ProducesLedgerProblems]
public sealed class FailingLedgerController : ControllerBase
{
    [HttpGet("/controller/accept")]
    public IActionResult Accept() => throw LedgerFailures.CommittedUndecodable(updateId: "update-9", commandId: "cmd-17");
}

public class LedgerProblemEndpointConventionTests
{
    [Fact]
    public async Task A_minimal_api_endpoint_declares_the_ledger_problem_responses_in_the_OpenAPI_document()
    {
        await using var host = await StartWithOpenApiAsync(app =>
            app.MapGet("/accept", (Func<IResult>)(() => throw LedgerFailures.CommittedUndecodable("update-9", "cmd-17")))
                .ProducesLedgerProblems());

        var (_, document) = await host.GetProblemAsync("/openapi/v1.json");

        var responses = document.GetProperty("paths").GetProperty("/accept").GetProperty("get").GetProperty("responses");
        responses.EnumerateObject().Select(response => response.Name).Order()
            .Should().Equal("202", "400", "403", "404", "409", "502");
        responses.GetProperty("202").GetProperty("content").EnumerateObject().Select(media => media.Name)
            .Should().Equal("application/problem+json");
        responses.GetProperty("409").GetProperty("content").EnumerateObject().Select(media => media.Name)
            .Should().Equal("application/problem+json");
    }

    [Fact]
    public async Task The_declared_202_response_publishes_the_command_and_update_id_members()
    {
        await using var host = await StartWithOpenApiAsync(app =>
            app.MapGet("/accept", (Func<IResult>)(() => throw LedgerFailures.CommittedUndecodable("update-9", "cmd-17")))
                .ProducesLedgerProblems());

        var (_, document) = await host.GetProblemAsync("/openapi/v1.json");

        var schemaRef = document.GetProperty("paths").GetProperty("/accept").GetProperty("get")
            .GetProperty("responses").GetProperty("202").GetProperty("content")
            .GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString();
        schemaRef.Should().Be("#/components/schemas/CommittedCommandProblem");
        var members = document.GetProperty("components").GetProperty("schemas")
            .GetProperty("CommittedCommandProblem").GetProperty("properties");
        members.EnumerateObject().Select(member => member.Name).Should().Contain(["commandId", "updateId"]);
    }

    [Fact]
    public async Task A_controller_action_failing_with_a_committed_command_answers_202_through_the_same_handler()
    {
        await using var host = await LedgerApiHost.StartAsync(
            app => app.MapControllers(),
            services => services.AddControllers());

        var (response, problem) = await host.GetProblemAsync("/controller/accept");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        problem.GetProperty("commandId").GetString().Should().Be("cmd-17");
        problem.GetProperty("updateId").GetString().Should().Be("update-9");
    }

    [Fact]
    public async Task A_controller_marked_with_the_attribute_declares_the_ledger_problem_responses_in_the_OpenAPI_document()
    {
        await using var host = await LedgerApiHost.StartAsync(
            app =>
            {
                app.MapOpenApi();
                app.MapControllers();
            },
            services =>
            {
                services.AddControllers();
                services.AddOpenApi();
            });

        var (_, document) = await host.GetProblemAsync("/openapi/v1.json");

        var responses = document.GetProperty("paths").GetProperty("/controller/accept").GetProperty("get").GetProperty("responses");
        responses.EnumerateObject().Select(response => response.Name).Order()
            .Should().Equal("202", "400", "403", "404", "409", "502");
    }

    private static Task<LedgerApiHost> StartWithOpenApiAsync(Action<WebApplication> mapEndpoints) =>
        LedgerApiHost.StartAsync(
            app =>
            {
                app.MapOpenApi();
                mapEndpoints(app);
            },
            services => services.AddOpenApi());
}
