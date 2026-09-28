// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Identifies the parties on whose behalf a command submission is authorized.
/// Carries the <c>actAs</c> (authorizing) party set and the optional <c>readAs</c>
/// (read-only visibility) party set that propagate to <c>Commands.act_as</c> /
/// <c>Commands.read_as</c> in the Ledger API gRPC payload.
/// </summary>
/// <remarks>
/// <para>
/// At least one party must be present in <see cref="ActAs"/>; an empty set is rejected
/// with <see cref="ArgumentException"/>. <see cref="ReadAs"/> defaults to empty. Each
/// caller-supplied set is snapshotted into an immutable
/// <see cref="System.Collections.Frozen.FrozenSet{T}"/> at construction so caller
/// mutations after the fact don't bleed in, and so a consumer who casts
/// <see cref="ActAs"/> / <see cref="ReadAs"/> back to a concrete collection type
/// still cannot mutate the snapshot.
/// </para>
/// <para>
/// An implicit conversion from <see cref="Party"/> preserves the single-party
/// ergonomic at every existing call site. There is deliberately no
/// <see cref="string"/> conversion: callers construct parties explicitly with
/// <c>new Party(...)</c> so a bare string can never be mistaken for an authorized
/// submitter.
/// </para>
/// <para>
/// This is the canonical home of <c>SubmitterInfo</c>: <c>Party</c> already lives in
/// <c>Daml.Runtime</c>, so command submitters belong here too. Ledger-client
/// transport implementations consume this type via their <c>Daml.Runtime</c>
/// package reference.
/// </para>
/// <para>
/// As JSON it is the object <c>{"ActAs":["alice"],"ReadAs":["bob"]}</c> — two arrays of the
/// bare party strings <see cref="Party"/> itself travels as, under whatever
/// <see cref="System.Text.Json.JsonSerializerOptions.PropertyNamingPolicy"/> the caller
/// serializes the surrounding record with. Without the converter the get-only
/// <see cref="ActAs"/> and <see cref="ReadAs"/> have no setter for
/// <see cref="System.Text.Json"/> to assign through, so a read produced a default value that
/// threw on first use rather than the submitter that was written.
/// </para>
/// </remarks>
[JsonConverter(typeof(SubmitterInfoJsonConverter))]
public readonly record struct SubmitterInfo
{
    /// <remarks>
    /// A frozen set is genuinely immutable: unlike a <c>HashSet</c> exposed as
    /// <see cref="IReadOnlySet{T}"/>, there is no underlying mutable collection a consumer can
    /// cast back to and mutate.
    /// </remarks>
    private static readonly IReadOnlySet<Party> EmptyParties = FrozenSet<Party>.Empty;

    private readonly IReadOnlySet<Party>? _actAs;
    private readonly IReadOnlySet<Party>? _readAs;

    /// <summary>
    /// The set of parties on whose behalf the submission is authorized
    /// (<c>Commands.act_as</c>). Always non-empty.
    /// </summary>
    public IReadOnlySet<Party> ActAs => _actAs ?? throw new InvalidOperationException(
        "Cannot access ActAs of a default (uninitialized) SubmitterInfo. " +
        "Construct via the SubmitterInfo constructor or implicit conversion from Party.");

    /// <summary>
    /// The set of additional parties whose contracts are read-visible during command
    /// interpretation (<c>Commands.read_as</c>). Defaults to an empty set.
    /// </summary>
    public IReadOnlySet<Party> ReadAs => _readAs ?? EmptyParties;

    /// <summary>
    /// Creates a multi-party submitter set.
    /// </summary>
    /// <param name="actAs">The authorizing parties. Must be non-empty.</param>
    /// <param name="readAs">Optional read-only visibility parties. Defaults to empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="actAs"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="actAs"/> is empty.</exception>
    public SubmitterInfo(IReadOnlySet<Party> actAs, IReadOnlySet<Party>? readAs = null)
    {
        ArgumentNullException.ThrowIfNull(actAs);
        if (actAs.Count == 0)
        {
            throw new ArgumentException(
                "SubmitterInfo.ActAs must contain at least one party.", nameof(actAs));
        }

        _actAs = ValidatedFrozenSet(actAs, nameof(actAs));
        _readAs = readAs is null || readAs.Count == 0
            ? null
            : ValidatedFrozenSet(readAs, nameof(readAs));
    }

    /// <summary>
    /// Creates a single-party submitter set — equivalent to constructing
    /// with a one-element set but avoids the intermediate collection allocation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="singleActAs"/> is <c>default(Party)</c>.
    /// </exception>
    public SubmitterInfo(Party singleActAs, IReadOnlySet<Party>? readAs = null)
    {
        _ = singleActAs.Value;
        _actAs = FrozenSet.Create(singleActAs);
        _readAs = readAs is null || readAs.Count == 0
            ? null
            : ValidatedFrozenSet(readAs, nameof(readAs));
    }

    private static FrozenSet<Party> ValidatedFrozenSet(IReadOnlySet<Party> source, string paramName)
    {
        foreach (var party in source)
        {
            try
            {
                _ = party.Value;
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException(
                    $"SubmitterInfo.{paramName} contains a default (uninitialized) Party.", paramName, ex);
            }
        }
        return source.ToFrozenSet();
    }

    /// <summary>
    /// Implicitly converts a single <see cref="Party"/> into a
    /// <see cref="SubmitterInfo"/> with that single <c>actAs</c> party and no
    /// <c>readAs</c> parties.
    /// </summary>
    public static implicit operator SubmitterInfo(Party singleActAs) =>
        new(singleActAs);

    /// <summary>
    /// Compares two <see cref="SubmitterInfo"/> instances by the contents of
    /// their <see cref="ActAs"/> and <see cref="ReadAs"/> sets. The
    /// record-struct-synthesized equality compares the backing
    /// <see cref="IReadOnlySet{T}"/> fields by reference — that's a footgun
    /// for a value type, so we override it explicitly here.
    /// </summary>
    public bool Equals(SubmitterInfo other) =>
        SetsEqual(_actAs, other._actAs) && SetsEqual(_readAs, other._readAs);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        SetHash(_actAs),
        SetHash(_readAs));

    private static bool SetsEqual(IReadOnlySet<Party>? a, IReadOnlySet<Party>? b)
    {
        var ac = a?.Count ?? 0;
        var bc = b?.Count ?? 0;
        if (ac != bc)
        {
            return false;
        }
        if (ac == 0)
        {
            return true;
        }
        foreach (var p in a!)
        {
            if (!b!.Contains(p))
            {
                return false;
            }
        }
        return true;
    }

    private static int SetHash(IReadOnlySet<Party>? set)
    {
        if (set is null || set.Count == 0)
        {
            return 0;
        }
        var h = 0;
        foreach (var p in set)
        {
            h ^= p.GetHashCode();
        }
        return h;
    }
}

/// <summary>
/// System.Text.Json converter for <see cref="SubmitterInfo"/>. Writes the two party sets as JSON
/// arrays under the <see cref="SubmitterInfo.ActAs"/> and <see cref="SubmitterInfo.ReadAs"/>
/// property names, so a submitter reads back as the one that was written rather than as a value
/// whose <see cref="SubmitterInfo.ActAs"/> throws where it is used.
/// </summary>
internal sealed class SubmitterInfoJsonConverter : JsonConverter<SubmitterInfo>
{
    private const string TypeName = nameof(SubmitterInfo);

    /// <remarks>
    /// The family's posture on null: a JSON null is rejected wherever the declared type forbids
    /// one and read as absent wherever it permits one — a slot declared <c>SubmitterInfo?</c>
    /// short-circuits null before reaching here.
    /// </remarks>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    public override SubmitterInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException(
                $"{TypeName} cannot be null; declare the slot as {TypeName}? to accept an absent one.");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected object token for {TypeName}, got {reader.TokenType}.");
        }

        var actAsName = PropertyName(nameof(SubmitterInfo.ActAs), options);
        var readAsName = PropertyName(nameof(SubmitterInfo.ReadAs), options);
        var comparison = options.PropertyNameCaseInsensitive
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        IReadOnlySet<Party>? actAs = null;
        IReadOnlySet<Party>? readAs = null;

        while (true)
        {
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.");
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var property = reader.GetString()!;
            if (!reader.Read())
            {
                throw new JsonException($"Unexpected end of JSON while reading {TypeName}.{property}.");
            }

            if (string.Equals(property, actAsName, comparison))
            {
                actAs = ReadParties(ref reader, actAsName, options);
            }
            else if (string.Equals(property, readAsName, comparison))
            {
                readAs = ReadParties(ref reader, readAsName, options);
            }
            else
            {
                reader.Skip();
            }
        }

        if (actAs is null)
        {
            throw new JsonException($"{TypeName} requires '{actAsName}'; the payload does not carry it.");
        }

        try
        {
            return new SubmitterInfo(actAs, readAs);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"Invalid {TypeName}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SubmitterInfo value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlySet<Party> actAs;
        try
        {
            actAs = value.ActAs;
        }
        catch (InvalidOperationException ex)
        {
            throw new JsonException($"Cannot serialize an uninitialized {TypeName}.", ex);
        }

        writer.WriteStartObject();
        WriteParties(writer, PropertyName(nameof(SubmitterInfo.ActAs), options), actAs, options);
        WriteParties(writer, PropertyName(nameof(SubmitterInfo.ReadAs), options), value.ReadAs, options);
        writer.WriteEndObject();
    }

    private static IReadOnlySet<Party> ReadParties(
        ref Utf8JsonReader reader, string property, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException($"{TypeName}.{property} cannot be null; write [] for no parties.");
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException(
                $"Expected array token for {TypeName}.{property}, got {reader.TokenType}.");
        }

        try
        {
            return JsonSerializer.Deserialize<HashSet<Party>>(ref reader, options)!;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot read {TypeName}.{property}: {ex.Message}", ex);
        }
    }

    private static void WriteParties(
        Utf8JsonWriter writer, string property, IReadOnlySet<Party> parties, JsonSerializerOptions options)
    {
        writer.WritePropertyName(property);
        try
        {
            JsonSerializer.Serialize(writer, parties, options);
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot write {TypeName}.{property}: {ex.Message}", ex);
        }
    }

    private static string PropertyName(string clrName, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;
}
