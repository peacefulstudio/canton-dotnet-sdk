// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Commands;

/// <summary>
/// A command deduplication period, in both directions of the Ledger API: the period a
/// submission asks the participant to deduplicate over
/// (<see cref="CommandsSubmission.DeduplicationPeriod"/>, projected onto the
/// <c>Commands.deduplication_period</c> oneof), and the period a participant reported for a
/// completed command. Discriminated union: callers <c>switch</c> on the concrete subtype, so a
/// period is either an <see cref="Offset"/> or a <see cref="Duration"/> and never both — the
/// protobuf <c>deduplication_period</c> oneof made unrepresentable to misread.
/// </summary>
/// <remarks>
/// Serializes through <see cref="System.Text.Json"/> as the arm's own object with a
/// <c>"$case"</c> discriminator naming the arm, e.g. <c>{"$case":"Offset","Start":42}</c> (a
/// CLR round-trip contract, not the Daml-LF wire encoding). It names
/// <see cref="DeduplicationPeriodJsonConverterFactory"/> in a <see cref="JsonConverterAttribute"/>,
/// so it converts on bare <see cref="JsonSerializerOptions"/> with no registration.
/// </remarks>
[JsonConverter(typeof(DeduplicationPeriodJsonConverterFactory))]
public abstract record DeduplicationPeriod
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected DeduplicationPeriod() { }

    /// <summary>
    /// The period starts at a completion-stream offset.
    /// </summary>
    /// <param name="Start">The offset the period starts after (exclusive);
    /// <see cref="LedgerOffset.Begin"/> when it starts at participant begin.</param>
    public sealed record Offset(LedgerOffset Start) : DeduplicationPeriod;

    /// <summary>
    /// The period is a length of time, interpreted relative to the participant's clock at
    /// some point during the submission's processing.
    /// </summary>
    /// <param name="Length">The length of the period.</param>
    public sealed record Duration(TimeSpan Length) : DeduplicationPeriod;
}

internal sealed class DeduplicationPeriodJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(DeduplicationPeriod) || typeToConvert.BaseType == typeof(DeduplicationPeriod);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new DeduplicationPeriodJsonConverter();
}

internal sealed class DeduplicationPeriodJsonConverter : JsonConverter<DeduplicationPeriod>
{
    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(DeduplicationPeriod.Offset)] = typeof(DeduplicationPeriod.Offset),
        [nameof(DeduplicationPeriod.Duration)] = typeof(DeduplicationPeriod.Duration),
    };

    public override DeduplicationPeriod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<DeduplicationPeriod>(ref reader, options, Cases, nameof(DeduplicationPeriod));

    public override void Write(Utf8JsonWriter writer, DeduplicationPeriod value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, nameof(DeduplicationPeriod));
}
