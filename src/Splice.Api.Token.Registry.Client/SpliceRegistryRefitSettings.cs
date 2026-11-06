// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Refit;

namespace Splice.Api.Token.Registry.Client;

/// <summary>
/// The <see cref="RefitSettings"/> every Token Standard registry interface is built with. The
/// generated contracts name their JSON properties themselves, so the settings only add the one
/// rule the registry API depends on: an unset optional field is omitted from the request body
/// rather than sent as <c>null</c>.
/// </summary>
public static class SpliceRegistryRefitSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Creates the <see cref="RefitSettings"/> to build a registry interface with when the
    /// dependency injection registration is not used, for example
    /// <c>RestService.For&lt;IAllocationV2Api&gt;(httpClient, SpliceRegistryRefitSettings.Create())</c>.
    /// </summary>
    public static RefitSettings Create() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(SerializerOptions),
    };
}
