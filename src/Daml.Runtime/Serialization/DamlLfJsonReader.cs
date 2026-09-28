// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Decodes a top-level LF-JSON document against the shape of a generated Daml record, producing a
/// <see cref="DamlRecord"/> whose field values carry their true Daml types — a Daml
/// <c>Party</c> field arrives as <see cref="DamlParty"/> rather than the
/// <see cref="DamlText"/> an untyped decode would yield. Callers hand the result to the
/// generated <c>FromRecord</c>.
/// </summary>
/// <remarks>
/// The returned record's <see cref="DamlRecord.RecordId"/> is always <see langword="null"/>:
/// LF-JSON carries no type identifier, and generated <c>FromRecord</c> reads fields by label.
/// Each entry point applies the decode limits and hands the document to the record's emitted
/// decoder, which composes the readers on <see cref="DamlLfJsonDecoders"/>.
/// </remarks>
public static class DamlLfJsonReader
{
    /// <summary>
    /// Decodes an already-parsed LF-JSON object against the shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml record type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON object to decode.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="DamlLfJsonDecoders.ReadRecord{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlRecord ReadRecord<T>(JsonElement json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord<T> =>
        T.__ReadDamlLfJson(json, DamlLfJsonDecodeContext.Root(typeof(T).Name, limits));

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml record type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A record whose field values carry their Daml types.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <remarks>JSON properties the target type does not declare are ignored by design, for tolerance of payloads produced by newer Daml package versions.</remarks>
    /// <seealso cref="DamlLfJsonDecoders.ReadRecord{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlRecord ReadRecord<T>(string json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlRecord<T>
    {
        var context = ParseRoot<T>(json, limits, out var document);
        using (document)
        {
            return T.__ReadDamlLfJson(document.RootElement, context);
        }
    }

    /// <summary>
    /// Decodes an already-parsed LF-JSON value against the shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml variant type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON variant value to decode.</param>
    /// <param name="limits">
    /// Decode limits; the shared hardened defaults apply when omitted.
    /// <see cref="DamlJsonDeserializationLimits.MaxInputCharacters"/> is accepted and ignored here:
    /// the caller already parsed the document, so the parse-time size, duplicate-property and
    /// parse-depth caps were theirs to apply. What this overload bounds is the reader's own
    /// allocation amplification and recursion depth while walking an already-materialized
    /// document — decode bounds, not a boundary defence against hostile input.
    /// </param>
    /// <returns>A variant whose selected arm carries its Daml type.</returns>
    /// <exception cref="JsonException">The JSON does not match the expected shape, or a decode limit is exceeded.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <seealso cref="DamlLfJsonDecoders.ReadVariant{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlVariant ReadVariant<T>(JsonElement json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlVariant<T> =>
        DamlLfJsonDecoders.ReadVariant<T>(json, DamlLfJsonDecodeContext.Root(typeof(T).Name, limits));

    /// <summary>
    /// Parses LF-JSON under the shared hardened document options and decodes it against the
    /// shape of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The generated Daml variant type describing the expected shape.</typeparam>
    /// <param name="json">The LF-JSON text to parse and decode.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted.</param>
    /// <returns>A variant whose selected arm carries its Daml type.</returns>
    /// <exception cref="JsonException">The JSON is malformed, does not match the expected shape, or exceeds a limit.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    /// <seealso cref="DamlLfJsonDecoders.ReadVariant{T}(JsonElement, DamlLfJsonDecodeContext)"/>
    public static DamlVariant ReadVariant<T>(string json, DamlJsonDeserializationLimits? limits = null)
        where T : IDamlVariant<T>
    {
        var context = ParseRoot<T>(json, limits, out var document);
        using (document)
        {
            return DamlLfJsonDecoders.ReadVariant<T>(document.RootElement, context);
        }
    }

    /// <summary>
    /// Validates <paramref name="limits"/>, applies the shared hardened input-size guard to
    /// <paramref name="json"/>, and parses it into <paramref name="document"/>, returning the root
    /// decode context for <typeparamref name="T"/>.
    /// </summary>
    private static DamlLfJsonDecodeContext ParseRoot<T>(
        string json, DamlJsonDeserializationLimits? limits, out JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(json);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.EnsureWithinInputLimit(json, effectiveLimits);
        document = JsonDocument.Parse(json, DamlJsonSerializer.DocumentOptions);
        return DamlLfJsonDecodeContext.Root(typeof(T).Name, effectiveLimits);
    }
}
