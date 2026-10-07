// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Canton.Ledger.Abstractions;

namespace Canton.Ledger.Testing;

internal sealed class PqsFilterEvaluator : IPqsFilterVisitor<bool?>, IPqsPathVisitor<PqsPathValue>
{
    private readonly string contractId;
    private readonly Dictionary<PqsScope, JsonElement> scopeValues;

    private PqsFilterEvaluator(string contractId, JsonElement payload)
    {
        this.contractId = contractId;
        scopeValues = new() { [PqsScope.Payload] = payload };
    }

    public static bool Matches(PqsFilter filter, string contractId, JsonElement payload) =>
        filter.Accept(new PqsFilterEvaluator(contractId, payload)) == true;

    public bool? Visit(PqsAll filter)
    {
        bool? result = true;
        foreach (var operand in filter.Filters)
        {
            var value = operand.Accept(this);
            if (value == false) return false;
            if (value is null) result = null;
        }

        return result;
    }

    public bool? Visit(PqsAny filter)
    {
        bool? result = false;
        foreach (var operand in filter.Filters)
        {
            var value = operand.Accept(this);
            if (value == true) return true;
            if (value is null) result = null;
        }

        return result;
    }

    public bool? Visit(PqsNot filter) => !(filter.Operand.Accept(this) ?? false);

    public bool? Visit(PqsCompare filter)
    {
        var path = filter.Path.Accept(this);
        if (!path.GuardPasses) return null;

        var left = path.TypedValue(filter.Operand.Kind, contractId);
        return Compare(left, filter.Comparison, PqsLeafValues.FromOperand(filter.Operand));
    }

    public bool? Visit(PqsOptionalPresence filter)
    {
        var path = filter.Optional.Accept(this);
        if (!path.GuardPasses) return null;
        return filter.IsSome ? IsSome(path.Json, filter.ListEncoded) : IsNone(path.Json, filter.ListEncoded);
    }

    public bool? Visit(PqsNotNull filter)
    {
        var path = filter.Path.Accept(this);
        return path.GuardPasses ? path.Json is not null : null;
    }

    public bool? Visit(PqsListAny filter)
    {
        var list = filter.List.Accept(this);
        if (!list.GuardPasses) return null;
        return Elements(list).Any(element => ElementSatisfies(filter.Element, element, filter.Condition) == true);
    }

    public bool? Visit(PqsListAll filter)
    {
        var list = filter.List.Accept(this);
        if (!list.GuardPasses) return null;
        return Elements(list).All(element => ElementSatisfies(filter.Element, element, filter.Condition) == true);
    }

    public PqsPathValue Visit(PqsScopeRoot path) =>
        PqsPathValue.Root(scopeValues[path.Scope], path.Scope.AliasOrdinal is { } ordinal ? $"element{ordinal}" : "payload");

    public PqsPathValue Visit(PqsField path) => path.Parent.Accept(this).Field(path.Name);

    public PqsPathValue Visit(PqsFirstElement path) => path.Optional.Accept(this).FirstElement();

    public PqsPathValue Visit(PqsSomeCast path)
    {
        var optional = path.Optional.Accept(this);
        return optional.Guarded(IsSome(optional.Json, path.ListEncoded) == true);
    }

    public PqsPathValue Visit(PqsConstructorCast path)
    {
        var variant = path.Variant.Accept(this);
        return variant.Guarded(variant.Field("tag").Text == path.Tag);
    }

    public PqsPathValue Visit(PqsMapEntry path)
    {
        var map = path.Map.Accept(this);
        var entry = path.Key.Kind == PqsLeafKind.Text
            ? map.Member(path.Key.Value.ToString()!) ?? GenMapValue(map, path.Key)
            : GenMapValue(map, path.Key);
        return map.Derived(entry, $"[{path.Key.Value}]");
    }

    public PqsPathValue Visit(PqsDefaulted path) => path.Value.Accept(this).DefaultingTo(path.Default);

    private JsonElement? GenMapValue(PqsPathValue map, PqsOperand key)
    {
        if (map.Json is not { ValueKind: JsonValueKind.Array } entries) return null;

        var wanted = PqsLeafValues.FromOperand(key);
        foreach (var entry in entries.EnumerateArray())
        {
            var keyText = PqsPathValue.Root(entry, map.Description).FirstElement();
            var typedKey = keyText.TypedValue(key.Kind, contractId);
            if (typedKey is not null && typedKey.Equals(wanted))
                return PqsPathValue.Root(entry, map.Description).ElementAt(1).Json;
        }

        return null;
    }

    private static IEnumerable<JsonElement> Elements(PqsPathValue list) =>
        list.Json is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private bool? ElementSatisfies(PqsScope scope, JsonElement element, PqsFilter? condition)
    {
        if (condition is null) return true;

        scopeValues[scope] = element;
        try
        {
            return condition.Accept(this);
        }
        finally
        {
            scopeValues.Remove(scope);
        }
    }

    private static bool? Compare(IComparable? left, PqsComparison comparison, IComparable right)
    {
        if (left is null) return comparison == PqsComparison.NotEqual ? true : null;

        return comparison switch
        {
            PqsComparison.Equal => left.Equals(right),
            PqsComparison.NotEqual => !left.Equals(right),
            PqsComparison.LessThan => left.CompareTo(right) < 0,
            PqsComparison.LessThanOrEqual => left.CompareTo(right) <= 0,
            PqsComparison.GreaterThan => left.CompareTo(right) > 0,
            PqsComparison.GreaterThanOrEqual => left.CompareTo(right) >= 0,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null),
        };
    }

    private static bool? IsSome(JsonElement? json, bool listEncoded)
    {
        if (json is not { } element) return null;
        return listEncoded
            ? element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0
            : element.ValueKind != JsonValueKind.Null;
    }

    private static bool? IsNone(JsonElement? json, bool listEncoded)
    {
        if (json is not { } element) return true;
        return listEncoded
            ? element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 0
            : element.ValueKind == JsonValueKind.Null;
    }
}
