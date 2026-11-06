// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Splice.Api.Token.Registry.Client.Tests;

internal static class RegistryClientFixture
{
    internal static (TApi Api, RecordingHandler Handler) Resolve<TApi>(HttpStatusCode status, string responseJson)
        where TApi : notnull
    {
        var handler = new RecordingHandler(status, responseJson);
        var services = new ServiceCollection();
        services.AddSpliceTokenRegistryClients(new Uri("https://registry.example/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<TApi>(), handler);
    }
}
