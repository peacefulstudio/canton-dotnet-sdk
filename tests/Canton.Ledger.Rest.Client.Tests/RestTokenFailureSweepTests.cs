// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing.Helpers;
using Microsoft.Extensions.Options;

namespace Canton.Ledger.Rest.Client.Tests;

public sealed class RestTokenFailureSweepTests : TokenFailureSweepTests, IDisposable
{
    private readonly RecordingHttpHandler _participant = new();
    private StubHttpClientFactory? _factory;

    public void Dispose() => _factory?.Dispose();

    protected override byte[] PreparedTransaction { get; } = [0x0A, 0x03];

    protected override int RequestsReceived => _participant.Requests.Count;

    protected override (ICantonLedgerClient Ledger, IAdminClient Admin) CreateClients(ITokenProvider tokenProvider)
    {
        _factory = new StubHttpClientFactory(new BearerTokenHandler(tokenProvider) { InnerHandler = _participant });
        var ledger = new RestLedgerClient(
            _factory, Options.Create(new RestLedgerClientOptions { HttpAddress = "http://localhost:7575" }));
        return (ledger, new RestAdminClient(_factory));
    }

    protected override string? ServedWithoutAuthentication(string entryPoint) => entryPoint switch
    {
        "GetTimeAsync(1)" or "SetTimeAsync(3)" or "GetCommandStatusAsync(4)" or "ListKnownPackagesAsync(1)"
            or "PruneAsync(4)" or "UpdatePartyIdentityProviderIdAsync(4)" =>
            "the participant serves no route for this member over the JSON Ledger API, so the client raises NotSupportedException before any request",
        _ => null,
    };
}
