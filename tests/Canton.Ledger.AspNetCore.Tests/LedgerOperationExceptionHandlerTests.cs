// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Canton.Ledger.AspNetCore.Tests;

public class LedgerOperationExceptionHandlerTests
{
    [Fact]
    public async Task A_committed_but_undecodable_command_answers_202_problem_json_carrying_its_command_and_update_ids()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(
            LedgerFailures.CommittedUndecodable(updateId: "update-9", commandId: "cmd-17"));

        var (response, problem) = await host.GetProblemAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("status").GetInt32().Should().Be(202);
        problem.GetProperty("title").GetString().Should().Be("Accepted");
        problem.GetProperty("detail").GetString().Should().Be(
            "The command was committed but its result could not be read. Do not retry; re-read the resource.");
        problem.GetProperty("commandId").GetString().Should().Be("cmd-17");
        problem.GetProperty("updateId").GetString().Should().Be("update-9");
    }

    [Fact]
    public async Task A_committed_command_without_ids_carries_no_id_extensions()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(
            LedgerFailures.CommittedUndecodable(updateId: null, commandId: null));

        var (response, problem) = await host.GetProblemAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        problem.TryGetProperty("commandId", out _).Should().BeFalse();
        problem.TryGetProperty("updateId", out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(CommittedOutcomes))]
    public async Task A_committed_outcome_that_is_not_the_expected_single_result_answers_202(
        string outcomeKind, ExerciseOutcome<int> outcome)
    {
        _ = outcomeKind;
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Committed(outcome));

        var (response, problem) = await host.GetProblemAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        problem.GetProperty("commandId").GetString().Should().Be("cmd-3");
    }

    public static TheoryData<string, ExerciseOutcome<int>> CommittedOutcomes => new()
    {
        { "none", new ExerciseOutcome<int>.None { CommandId = "cmd-3" } },
        { "many", new ExerciseOutcome<int>.Many(["c1", "c2"]) { CommandId = "cmd-3" } },
    };

    [Fact]
    public async Task A_refused_command_carries_no_id_extensions()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Rejected(
            DamlErrorCategory.ContentionOnSharedResources, commandId: "cmd-17"));

        var (_, problem) = await host.GetProblemAsync("/fail");

        problem.TryGetProperty("commandId", out _).Should().BeFalse();
        problem.TryGetProperty("updateId", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS", 409, "Conflict")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "CONTRACT_NOT_FOUND", 409, "Conflict")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_CONTRACT_KEY", 409, "Conflict")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "USER_NOT_FOUND", 404, "Not Found")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "IDP_CONFIG_NOT_FOUND", 404, "Not Found")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "DAR_NOT_FOUND", 404, "Not Found")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "INCONSISTENT", 400, "Bad Request")]
    [InlineData(DamlErrorCategory.InvalidIndependentOfSystemState, "COMMAND_PREPROCESSING_FAILED", 400, "Bad Request")]
    [InlineData(DamlErrorCategory.InsufficientPermission, "PERMISSION_DENIED", 403, "Forbidden")]
    [InlineData(DamlErrorCategory.SecurityAlert, "EVENT_DESERIALIZATION_ERROR", 400, "Bad Request")]
    [InlineData(DamlErrorCategory.TransientServerFailure, "SERVICE_NOT_RUNNING", 502, "Ledger Unavailable")]
    [InlineData(DamlErrorCategory.SystemInternalAssumptionViolated, "SERVICE_INTERNAL_ERROR", 502, "Ledger Error")]
    [InlineData(DamlErrorCategory.InternalUnsupportedOperation, "NO_ENCRYPTED_PRIVATE_KEY_STORE_ERROR", 502, "Ledger Error")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateSeekAfterEnd, "OFFSET_AFTER_LEDGER_END", 502, "Ledger Error")]
    [InlineData(DamlErrorCategory.AuthInterceptorInvalidAuthenticationCredentials, "UNAUTHENTICATED", 502, "Ledger Error")]
    [InlineData(DamlErrorCategory.DeadlineExceededRequestStateUnknown, "REQUEST_TIME_OUT", 502, "Outcome Unknown")]
    [InlineData(DamlErrorCategory.DeadlineExceededRequestStateUnknown, "REQUEST_DEADLINE_EXCEEDED", 502, "Outcome Unknown")]
    [InlineData(DamlErrorCategory.Unknown, "UNCLASSIFIED", 502, "Outcome Unknown")]
    public async Task A_refused_command_answers_the_status_of_its_error_category(
        DamlErrorCategory category, string errorId, int expectedStatus, string expectedTitle)
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Rejected(category, errorId));

        var (response, problem) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(expectedStatus);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("status").GetInt32().Should().Be(expectedStatus);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData(DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS", "Contention on the contract; please retry.")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "CONTRACT_NOT_FOUND", "The contract was already consumed or never existed.")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists, "DUPLICATE_CONTRACT_KEY", "The contract this submission would create already exists.")]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing, "USER_NOT_FOUND", "The ledger has no such resource.")]
    [InlineData(DamlErrorCategory.InsufficientPermission, "PERMISSION_DENIED", "The ledger refused this submission's authority.")]
    [InlineData(DamlErrorCategory.TransientServerFailure, "SERVICE_NOT_RUNNING", "Ledger submission failed; please retry.")]
    [InlineData(DamlErrorCategory.SystemInternalAssumptionViolated, "SERVICE_INTERNAL_ERROR", "The ledger submission failed unexpectedly.")]
    public async Task A_refused_command_answers_a_detail_that_does_not_echo_the_ledger_message(
        DamlErrorCategory category, string errorId, string expectedDetail)
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Rejected(
            category, errorId, message: "package-id 0xdeadbeef party trader::abcd raw rpc detail"));

        var (_, problem) = await host.GetProblemAsync("/fail");

        problem.GetProperty("detail").GetString().Should().Be(expectedDetail);
    }

    [Theory]
    [InlineData(DamlErrorCategory.InvalidGivenCurrentSystemStateOther)]
    [InlineData(DamlErrorCategory.InvalidIndependentOfSystemState)]
    public async Task A_caller_fixable_400_returns_the_failure_message_as_its_detail(DamlErrorCategory category)
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Rejected(
            category, "GUARD_FAILED", message: "price must be positive"));

        var (_, problem) = await host.GetProblemAsync("/fail");

        problem.GetProperty("detail").GetString().Should().Be(
            $"ConnectionRequest.Accept: Daml error [{category}/GUARD_FAILED]: price must be positive");
    }

    [Fact]
    public async Task A_command_whose_outcome_is_unknown_answers_502_telling_the_caller_to_re_read_and_carries_its_command_id()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.OutcomeUnknown(commandId: "cmd-21"));

        var (response, problem) = await host.GetProblemAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        problem.GetProperty("title").GetString().Should().Be("Outcome Unknown");
        problem.GetProperty("detail").GetString().Should().Be(
            "The ledger did not confirm whether the command committed. Do not retry blindly; re-read the resource.");
        problem.GetProperty("commandId").GetString().Should().Be("cmd-21");
        problem.TryGetProperty("updateId", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(GrpcStatusCode.PermissionDenied, null, 403, "Forbidden")]
    [InlineData(GrpcStatusCode.InvalidArgument, null, 400, "Bad Request")]
    [InlineData(GrpcStatusCode.Unavailable, null, 502, "Outcome Unknown")]
    [InlineData(GrpcStatusCode.Unknown, DamlErrorCategory.InsufficientPermission, 403, "Forbidden")]
    public async Task A_redacted_refusal_keeps_the_status_its_transport_code_declares(
        GrpcStatusCode code, DamlErrorCategory? category, int expectedStatus, string expectedTitle)
    {
        await using var host = await LedgerApiHost.ThrowingAsync(
            LedgerFailures.RedactedRefusal(new TransportStatus.Grpc(code), category));

        var (response, problem) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(expectedStatus);
        problem.GetProperty("title").GetString().Should().Be(expectedTitle);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 502)]
    public async Task A_redacted_rest_refusal_keeps_the_status_its_http_code_declares(
        HttpStatusCode code, int expectedStatus)
    {
        await using var host = await LedgerApiHost.ThrowingAsync(
            LedgerFailures.RedactedRefusal(new TransportStatus.Http(code)));

        var (response, _) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(expectedStatus);
    }

    [Fact]
    public async Task A_retried_grpc_refusal_stays_an_unknown_outcome_with_its_command_id()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.RedactedRefusalAfterRetry(
            new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied), "cmd-31"));

        var (response, problem) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(502);
        problem.GetProperty("title").GetString().Should().Be("Outcome Unknown");
        problem.GetProperty("commandId").GetString().Should().Be("cmd-31");
    }

    [Fact]
    public async Task A_retried_rest_refusal_stays_an_unknown_outcome_with_its_command_id()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.RedactedRefusalAfterRetry(
            new TransportStatus.Http(HttpStatusCode.BadRequest), "cmd-32"));

        var (response, problem) = await host.GetProblemAsync("/fail");

        ((int)response.StatusCode).Should().Be(502);
        problem.GetProperty("title").GetString().Should().Be("Outcome Unknown");
        problem.GetProperty("commandId").GetString().Should().Be("cmd-32");
    }

    [Fact]
    public async Task An_exception_that_is_not_a_ledger_failure_is_left_to_the_default_500()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(new InvalidOperationException("boom"));

        var (response, problem) = await host.GetProblemAsync("/fail");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        problem.TryGetProperty("commandId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_committed_command_answers_202_problem_json_even_when_the_caller_accepts_only_html()
    {
        await using var host = await LedgerApiHost.ThrowingAsync(
            LedgerFailures.CommittedUndecodable(updateId: "update-9", commandId: "cmd-17"));

        var (response, problem) = await host.GetProblemAsync("/fail", accept: "text/html");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        problem.GetProperty("updateId").GetString().Should().Be("update-9");
    }

    [Fact]
    public async Task A_contended_command_is_logged_as_a_warning_naming_its_operation_and_the_status_answered()
    {
        var logs = new CapturedLogs();
        await using var host = await LedgerApiHost.StartAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS"))),
            services => services.AddSingleton<ILoggerProvider>(logs));

        await host.GetAsync("/fail");

        logs.Entries.Should().Contain((
            LogLevel.Warning,
            "Ledger operation ConnectionRequest.Accept failed (NotCommitted, LOCAL_VERDICT_LOCKED_CONTRACTS) on GET /fail; answering 409"));
    }

    [Fact]
    public async Task A_command_answered_502_is_logged_as_an_error()
    {
        var logs = new CapturedLogs();
        await using var host = await LedgerApiHost.StartAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.SystemInternalAssumptionViolated, "SERVICE_INTERNAL_ERROR"))),
            services => services.AddSingleton<ILoggerProvider>(logs));

        await host.GetAsync("/fail");

        logs.Entries.Should().Contain((
            LogLevel.Error,
            "Ledger operation ConnectionRequest.Accept failed (NotCommitted, SERVICE_INTERNAL_ERROR) on GET /fail; answering 502"));
    }

    [Fact]
    public async Task A_ledger_failure_marks_the_request_span_failed_and_tags_the_error_id_and_category()
    {
        using var activities = new CapturedActivities();
        await using var host = await LedgerApiHost.ThrowingAsync(LedgerFailures.Rejected(
            DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS",
            metadata: new Dictionary<string, string> { ["agreement_id"] = "agr-1" }));

        await host.GetAsync("/fail");

        var span = await activities.RequestActivityFailingWithAsync("LOCAL_VERDICT_LOCKED_CONTRACTS");
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("Contention on the contract; please retry.");
        span.GetTagItem("daml.error_id").Should().Be("LOCAL_VERDICT_LOCKED_CONTRACTS");
        span.GetTagItem("daml.error_category").Should().Be("ContentionOnSharedResources");
        span.GetTagItem("daml.error_metadata.agreement_id").Should().BeNull();
    }

    [Fact]
    public async Task A_ledger_failure_tags_the_error_metadata_when_the_host_opts_in()
    {
        using var activities = new CapturedActivities();
        await using var host = await LedgerApiHost.StartAsync(
            app => app.MapGet("/fail", (Func<IResult>)(() => throw LedgerFailures.Rejected(
                DamlErrorCategory.ContentionOnSharedResources, "LOCAL_VERDICT_LOCKED_CONTRACTS",
                metadata: new Dictionary<string, string> { ["agreement_id"] = "agr-1", ["swap_index"] = "0" }))),
            optIntoErrorMetadataTags: true);

        await host.GetAsync("/fail");

        var span = await activities.RequestActivityFailingWithAsync("LOCAL_VERDICT_LOCKED_CONTRACTS");
        span.GetTagItem("daml.error_metadata.agreement_id").Should().Be("agr-1");
        span.GetTagItem("daml.error_metadata.swap_index").Should().Be("0");
    }
}
