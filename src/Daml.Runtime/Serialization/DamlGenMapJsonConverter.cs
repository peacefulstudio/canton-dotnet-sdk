// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// The <see cref="System.Text.Json"/> converter a <see cref="DamlGenMap"/> declared as itself takes
/// from the attribute on that type: <c>{"Entries":[{"Key":…,"Value":…}]}</c>, each side a
/// <see cref="DamlValue"/> written by <see cref="DamlValueClrJsonConverter"/> with its own
/// <c>"$case"</c>. The same shape the <see cref="DamlGenMap"/> arm takes when declared as
/// <see cref="DamlValue"/>, without the arm's discriminator. Reading refuses a duplicate key with a
/// <see cref="JsonException"/>, and nesting is bounded by <see cref="JsonSerializerOptions.MaxDepth"/>.
/// </summary>
internal sealed class DamlGenMapJsonConverter : JsonConverter<DamlGenMap>
{
    public override DamlGenMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DamlValueClrJsonConverter.ReadGenMapDeclaredAsItself(ref reader, options);

    public override void Write(Utf8JsonWriter writer, DamlGenMap value, JsonSerializerOptions options) =>
        DamlValueClrJsonConverter.WriteGenMapDeclaredAsItself(writer, value, options);
}
