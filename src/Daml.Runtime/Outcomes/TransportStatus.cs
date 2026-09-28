// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Outcomes;

/// <summary>
/// What the transport said about a failed ledger call or stream. Discriminated union: callers
/// <c>switch</c> on the concrete subtype, so a gRPC status, an HTTP status, a call that got no
/// response and a response whose body could not be decoded are four distinct values and never
/// one integer read four ways.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Grpc"/> — the gRPC transport's own status for the call.</item>
///   <item><see cref="Http"/> — the HTTP status the participant actually answered with.</item>
///   <item><see cref="NoResponse"/> — the call never got a complete response: the connection
///   failed, or the deadline expired, before the participant finished answering.</item>
///   <item><see cref="UndecodableBody"/> — the participant answered, but the client could not
///   decode what it sent.</item>
/// </list>
/// Serializes through <see cref="System.Text.Json"/> as the arm's own object with a
/// <c>"$case"</c> discriminator naming the arm, e.g. <c>{"$case":"Grpc","StatusCode":14}</c>.
/// </remarks>
[JsonConverter(typeof(TransportStatusJsonConverterFactory))]
public abstract record TransportStatus
{
    /// <summary>Sealed; new variants live alongside the existing ones.</summary>
    private protected TransportStatus() { }

    /// <summary>The gRPC transport reported a status for the call.</summary>
    /// <param name="StatusCode">The gRPC status the call ended with.</param>
    public sealed record Grpc(GrpcStatusCode StatusCode) : TransportStatus;

    /// <summary>The participant answered over HTTP with a status the client treats as a failure.</summary>
    /// <param name="StatusCode">The HTTP status of the participant's response.</param>
    public sealed record Http(HttpStatusCode StatusCode) : TransportStatus;

    /// <summary>
    /// The call got no complete response to report a status from: the connection failed, or the
    /// deadline expired, before the participant finished answering — including partway through
    /// reading the response body.
    /// </summary>
    public sealed record NoResponse : TransportStatus;

    /// <summary>
    /// The participant answered, but the client could not decode the payload it sent — the call
    /// itself succeeded at the transport level.
    /// </summary>
    public sealed record UndecodableBody : TransportStatus;
}

internal sealed class TransportStatusJsonConverterFactory : JsonConverterFactory, IDiscriminatedUnionJsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(TransportStatus) || typeToConvert.BaseType == typeof(TransportStatus);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        new TransportStatusJsonConverter();
}

internal sealed class TransportStatusJsonConverter : JsonConverter<TransportStatus>
{
    private static readonly IReadOnlyDictionary<string, Type> Cases = new Dictionary<string, Type>
    {
        [nameof(TransportStatus.Grpc)] = typeof(TransportStatus.Grpc),
        [nameof(TransportStatus.Http)] = typeof(TransportStatus.Http),
        [nameof(TransportStatus.NoResponse)] = typeof(TransportStatus.NoResponse),
        [nameof(TransportStatus.UndecodableBody)] = typeof(TransportStatus.UndecodableBody),
    };

    public override TransportStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Read<TransportStatus>(ref reader, options, Cases, nameof(TransportStatus));

    public override void Write(Utf8JsonWriter writer, TransportStatus value, JsonSerializerOptions options) =>
        DiscriminatedUnionJson.Write(writer, value, options, nameof(TransportStatus));
}
