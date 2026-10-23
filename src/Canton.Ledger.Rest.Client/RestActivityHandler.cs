// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using Canton.Ledger.Kernel.Telemetry;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Emits an OpenTelemetry HTTP client span for every JSON Ledger API request, following the
/// kernel's <see cref="LedgerActivitySource"/> naming convention. Register the
/// source as <c>tracing.AddSource(LedgerActivitySourceNames.RestLedgerClient)</c>.
/// Span names and tags follow the OpenTelemetry HTTP semantic conventions, with <c>url.full</c>
/// sanitized of its query string and user info, which can carry party ids and credentials. A request built
/// by a Refit method or by the admin client carries its route template in <c>url.template</c>, and
/// <c>url.full</c> keeps that template in place of the path, since a path parameter such as <c>/v2/parties/{party}</c> names a party or user.
/// </summary>
internal sealed class RestActivityHandler : DelegatingHandler
{
    internal const string HttpRequestMethod = "http.request.method";
    internal const string ServerAddress = SemanticConventions.ServerAddress;
    internal const string ServerPort = SemanticConventions.ServerPort;
    internal const string UrlFull = "url.full";
    internal const string UrlTemplate = "url.template";

    internal static readonly HttpRequestOptionsKey<string> PathTemplateOption = new("Canton.Ledger.Rest.Client.PathTemplate");
    internal const string HttpResponseStatusCode = "http.response.status_code";
    internal const string ErrorType = SemanticConventions.ErrorType;

    private static readonly ActivitySource Source = LedgerActivitySource.Create<RestLedgerClient>();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity(request.Method.Method, ActivityKind.Client);
        SetRequestTags(activity, request);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            RecordResponse(activity, response);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag(ErrorType, exception.GetType().FullName);
            throw;
        }
    }

    private static void SetRequestTags(Activity? activity, HttpRequestMessage request)
    {
        if (activity is null || request.RequestUri is not { IsAbsoluteUri: true } uri) return;

        activity.SetTag(HttpRequestMethod, request.Method.Method);
        activity.SetTag(ServerAddress, uri.Host);
        activity.SetTag(ServerPort, uri.Port);

        var realPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        if (RouteTemplateOf(request) is { } routeTemplate)
        {
            var route = RedactedRoute.Of(realPath, routeTemplate);
            activity.SetTag(UrlFull, SchemeAndServer(uri) + route.Path);
            activity.SetTag(UrlTemplate, route.Template);
            return;
        }

        activity.SetTag(UrlFull, SchemeAndServer(uri) + "/" + realPath);
    }

    private static string? RouteTemplateOf(HttpRequestMessage request)
    {
        var refitKey = new HttpRequestOptionsKey<string>(HttpRequestMessageOptions.RelativePathTemplate);
        var template = request.Options.TryGetValue(PathTemplateOption, out var own) ? own
            : request.Options.TryGetValue(refitKey, out var refit) ? refit
            : null;
        if (template is null) return null;

        var queryStart = template.IndexOf('?', StringComparison.Ordinal);
        return queryStart < 0 ? template : template[..queryStart];
    }

    private static string SchemeAndServer(Uri uri) =>
        uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    private readonly record struct RedactedRoute(string Path, string Template)
    {
        internal static RedactedRoute Of(string realPath, string routeTemplate)
        {
            var realSegments = realPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var templateSegments = routeTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var prefixLength = realSegments.Length - templateSegments.Length;
            if (prefixLength < 0) return new RedactedRoute(routeTemplate, routeTemplate);

            var prefix = realSegments[..prefixLength];
            var redacted = templateSegments
                .Select((segment, index) => segment.Contains('{', StringComparison.Ordinal) ? segment : realSegments[prefixLength + index]);
            return new RedactedRoute(
                "/" + string.Join('/', prefix.Concat(redacted)),
                "/" + string.Join('/', prefix.Concat(templateSegments)));
        }
    }

    private static void RecordResponse(Activity? activity, HttpResponseMessage response)
    {
        if (activity is null) return;

        var statusCode = (int)response.StatusCode;
        activity.SetTag(HttpResponseStatusCode, statusCode);

        if (!response.IsSuccessStatusCode)
        {
            var statusCodeText = statusCode.ToString(CultureInfo.InvariantCulture);
            activity.SetStatus(ActivityStatusCode.Error, response.ReasonPhrase);
            activity.SetTag(ErrorType, statusCodeText);
        }
    }
}
