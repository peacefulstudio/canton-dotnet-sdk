// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using Canton.Ledger.Rest.Client.Raw;
using Refit;

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Spells a URL parameter the way the JSON Ledger API reads it. A <see cref="DateTimeOffset"/> renders as an
/// RFC 3339 UTC timestamp (<c>2026-11-04T13:05:07.5Z</c>), the <c>format: date-time</c> the specification
/// declares, rather than the culture-shaped <c>11/04/2026 13:05:07 +00:00</c> Refit's default produces for
/// <see cref="IStateServiceApi.ConvertRecordTimeToOffset"/>'s <c>record-time</c>. Every other value, booleans
/// included, takes Refit's <see cref="DefaultUrlParameterFormatter"/> spelling: the participant reads a
/// boolean such as <see cref="IDarApi.UploadDar"/>'s <c>vetAllPackages</c> case-insensitively.
/// </summary>
/// <remarks>
/// The delta is Refit's <see cref="DefaultUrlParameterFormatter"/> rather than the served document,
/// so this formatter is needed for as long as the participant refuses Refit's default timestamp spelling.
/// </remarks>
internal sealed class JsonUrlParameterFormatter : DefaultUrlParameterFormatter
{
    private const string Rfc3339UtcFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    /// <inheritdoc />
    public override string? Format(object? value, ICustomAttributeProvider attributeProvider, Type type) =>
        value is DateTimeOffset timestamp
            ? timestamp.UtcDateTime.ToString(Rfc3339UtcFormat, CultureInfo.InvariantCulture)
            : base.Format(value, attributeProvider, type);
}
