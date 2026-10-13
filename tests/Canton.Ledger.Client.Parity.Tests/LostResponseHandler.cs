// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// A transport-level tap between a client and a live participant that models a response lost on
/// the wire: the request reaches the participant and runs to completion, and the client sees the
/// connection fail instead of the answer. It also records, per exchange, what the participant sent.
/// </summary>
public sealed class LostResponseTap
{
    private readonly List<string> _exchanges = [];
    private string? _armedOperation;

    public IReadOnlyList<string> Exchanges => _exchanges;

    public void LoseNextResponseTo(string operation) => _armedOperation = operation;

    public bool TakeArmed(string path)
    {
        if (_armedOperation is not { } operation || !path.EndsWith(operation, StringComparison.Ordinal))
        {
            return false;
        }

        _armedOperation = null;
        return true;
    }

    public void Record(string exchange) => _exchanges.Add(exchange);

    public void Clear() => _exchanges.Clear();
}

public sealed class LostResponseHandler(
    LostResponseTap tap, HttpMessageHandler inner, Func<HttpResponseMessage> lostResponse) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        var lost = tap.TakeArmed(path);
        tap.Record(await DescribeAsync(path, response, lost, cancellationToken).ConfigureAwait(false));

        if (lost)
        {
            response.Dispose();
            return lostResponse();
        }

        return response;
    }

    private static async Task<string> DescribeAsync(
        string path, HttpResponseMessage response, bool lost, CancellationToken cancellationToken)
    {
        var description = new StringBuilder();
        description.Append(CultureInfo.InvariantCulture, $"{path.Split('/')[^1]} -> HTTP {(int)response.StatusCode}");
        foreach (var (name, values) in response.Headers.Concat(response.TrailingHeaders))
        {
            if (name.StartsWith("grpc-", StringComparison.Ordinal) && name != "grpc-status-details-bin")
            {
                description.Append(CultureInfo.InvariantCulture, $" {name}={string.Join(",", values)}");
            }
        }

        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            description.Append(CultureInfo.InvariantCulture, $" body={body}");
        }

        description.Append(lost ? " [response dropped]" : " [delivered]");
        return description.ToString();
    }
}
