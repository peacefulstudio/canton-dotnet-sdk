// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

internal readonly record struct PqsPathValue(
    JsonElement? Json,
    string Description,
    bool GuardPasses,
    PqsOperand? AbsentDefault)
{
    public static PqsPathValue Root(JsonElement json, string description) => new(json, description, true, null);

    public string? Text =>
        Json switch
        {
            null => null,
            { ValueKind: JsonValueKind.Null } => null,
            { ValueKind: JsonValueKind.String } text => text.GetString(),
            { } other => other.GetRawText(),
        };

    public PqsPathValue Field(string name) =>
        this with
        {
            Json = Member(name),
            Description = $"{Description}.{name}",
            AbsentDefault = null,
        };

    public PqsPathValue FirstElement() => ElementAt(0);

    public PqsPathValue ElementAt(int index) =>
        this with
        {
            Json = Json is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > index ? array[index] : null,
            Description = $"{Description}[{index}]",
            AbsentDefault = null,
        };

    public JsonElement? Member(string name) =>
        Json is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(name, out var member) ? member : null;

    public PqsPathValue Derived(JsonElement? json, string suffix) =>
        this with { Json = json, Description = Description + suffix, AbsentDefault = null };

    public PqsPathValue Guarded(bool condition) => this with { GuardPasses = GuardPasses && condition };

    public PqsPathValue DefaultingTo(PqsOperand absentDefault) => this with { AbsentDefault = absentDefault };

    public IComparable? TypedValue(PqsLeafKind kind, string contractId)
    {
        var typed = Text is { } text ? Cast(kind, text, contractId) : null;
        return typed ?? (AbsentDefault is { } absentDefault ? PqsLeafValues.FromOperand(absentDefault) : null);
    }

    private IComparable Cast(PqsLeafKind kind, string text, string contractId)
    {
        try
        {
            return PqsLeafValues.Cast(kind, text);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"FakePqsClient cannot evaluate the filter on contract '{contractId}': field '{Description}' holds " +
                $"'{text}', which PQS would fail to cast to {kind}.", ex);
        }
    }
}
