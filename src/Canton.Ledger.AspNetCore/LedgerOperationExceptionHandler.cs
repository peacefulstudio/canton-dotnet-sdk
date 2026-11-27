// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Daml.Ledger.Abstractions;
using Canton.Ledger.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.AspNetCore;

internal sealed partial class LedgerOperationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IOptions<LedgerProblemOptions> options,
    ILogger<LedgerOperationExceptionHandler>? logger = null) : IExceptionHandler
{
    private const string ProblemJsonContentType = "application/problem+json";
    private const string ErrorIdTag = "daml.error_id";
    private const string ErrorCategoryTag = "daml.error_category";
    private const string ErrorMetadataTagPrefix = "daml.error_metadata.";

    private readonly ILogger _logger = logger ?? NullLogger<LedgerOperationExceptionHandler>.Instance;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not LedgerOperationException failure)
            return false;

        var problem = LedgerProblem.For(failure, options.Value.IncludeLedgerErrorMessages);
        Log(httpContext, failure, problem);
        TagSpan(Activity.Current, failure, problem);

        var details = new ProblemDetails { Status = problem.Status, Title = problem.Title, Detail = problem.Detail };
        AddIfPresent(details, "commandId", problem.CommandId);
        AddIfPresent(details, "updateId", problem.UpdateId);

        httpContext.Response.StatusCode = problem.Status;
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details,
            Exception = failure,
        }).ConfigureAwait(false);

        if (!written)
            await httpContext.Response.WriteAsJsonAsync(details, options: null, ProblemJsonContentType, cancellationToken)
                .ConfigureAwait(false);

        return true;
    }

    private static void AddIfPresent(ProblemDetails details, string name, string? value)
    {
        if (value is not null)
            details.Extensions[name] = value;
    }

    private void TagSpan(Activity? span, LedgerOperationException failure, LedgerProblem problem)
    {
        if (span is null)
            return;

        span.SetStatus(ActivityStatusCode.Error, problem.Detail);

        if (options.Value.EmitErrorMetadataSpanTags)
        {
            foreach (var (key, value) in failure.Metadata ?? new Dictionary<string, string>())
                span.SetTag(ErrorMetadataTagPrefix + key, value);
        }

        if (ErrorCodeId.OrNull(failure.ErrorId) is { } errorCode)
            span.SetTag(ErrorIdTag, errorCode);
        if (failure.Category is not null)
            span.SetTag(ErrorCategoryTag, failure.Category.ToString());
    }

    private void Log(HttpContext httpContext, LedgerOperationException failure, LedgerProblem problem)
    {
        var level = problem.Status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning;
        if (!_logger.IsEnabled(level))
            return;

        var loggedException = failure.RedactingUnless(options.Value.IncludeLedgerErrorMessages);
        var loggedErrorId = LoggedErrorId(failure.ErrorId);
        LedgerFailureAnswered(
            _logger, level, loggedException,
            failure.Operation ?? "unknown", failure.CommitState, loggedErrorId,
            httpContext.Request.Method, httpContext.Request.Path, problem.Status);
    }

    private static string LoggedErrorId(string? errorId) => errorId switch
    {
        null => "none",
        _ => ErrorCodeId.OrNull(errorId) ?? "unrecognised",
    };

    [LoggerMessage(Message = "Ledger operation {Operation} failed ({CommitState}, {ErrorId}) on {Method} {Path}; answering {StatusCode}")]
    private static partial void LedgerFailureAnswered(
        ILogger logger, LogLevel level, Exception exception, string operation, CommitState commitState,
        string errorId, string method, PathString path, int statusCode);
}
