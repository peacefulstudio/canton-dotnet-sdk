// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Daml.Runtime.Contracts;
using Daml.Runtime.Stdlib;

namespace Daml.Runtime.Data;

/// <summary>
/// Helpers for unwrapping <see cref="DamlValue"/> instances into CLR types.
/// </summary>
public static class DamlValueExtensions
{
    /// <summary>
    /// Normalizes a value into a <see cref="DamlOptional"/>: an existing
    /// <see cref="DamlOptional"/> passes through unchanged, a <see cref="DamlOptionalChain"/>
    /// level becomes the <see cref="DamlOptional"/> carrying the same value, and any other value
    /// is wrapped as Some. Ledger JSON flattens Some to the inner value, so
    /// schema-aware readers use this to recover the Optional wrapper that
    /// <see cref="DamlValue.As{T}"/> would reject. A chain level is already an Optional, in the
    /// array encoding rather than the flat one, so it is never wrapped as Some.
    /// </summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The value as a <see cref="DamlOptional"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static DamlOptional AsOptional(this DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            DamlOptional optional => optional,
            DamlOptionalChain chain => new DamlOptional(chain.Value),
            _ => DamlOptional.Some(value),
        };
    }

    /// <summary>
    /// Converts a <see cref="DamlValue"/> to a CLR type. Can be invoked either as an extension
    /// method (<c>value.FromDamlValue&lt;T&gt;()</c>) or as a static call
    /// (<c>DamlValueExtensions.FromDamlValue&lt;T&gt;(value)</c>).
    /// </summary>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    /// <item>If <typeparamref name="TResult"/> is assignable from <paramref name="value"/>'s runtime type,
    /// the original instance is returned. This takes precedence over every other branch, so
    /// <c>FromDamlValue&lt;object&gt;(DamlUnit.Instance)</c> returns the unit singleton, not <c>null</c>.</item>
    /// <item>If <paramref name="value"/> is <see cref="DamlUnit"/>: returns <c>default(TResult)</c>
    /// — which is <c>null</c> for reference types and <see cref="Nullable{T}"/>. Throws
    /// <see cref="NotSupportedException"/> for non-nullable value types.</item>
    /// <item>Primitive unwrapping: <c>string</c> (from <see cref="DamlText"/>, <see cref="DamlParty"/>,
    /// or <see cref="DamlContractId"/>), <c>long</c>, <c>bool</c>, <c>decimal</c>, <c>DateOnly</c>,
    /// <c>DateTimeOffset</c>, and <see cref="Party"/>. Each primitive branch also accepts
    /// <see cref="Nullable{T}"/> of the same underlying type.</item>
    /// <item><see cref="DamlContractId"/> → <see cref="ContractId{T}"/> via reflection.</item>
    /// <item><see cref="DamlOptional"/> or <see cref="DamlOptionalChain"/> to a
    /// <see cref="Optional{T}"/> target: <see cref="Optional{T}.None"/> when empty, otherwise
    /// <see cref="Optional{T}.Some"/> of the carried value converted by these same rules, so
    /// nested Optionals keep their levels.</item>
    /// <item><see cref="DamlOptional"/> or <see cref="DamlOptionalChain"/> to any other target: an empty
    /// level is <c>default(TResult)</c> — <c>null</c> for reference types and <see cref="Nullable{T}"/>, and a
    /// <see cref="NotSupportedException"/> for a non-nullable value type; a present one is its carried
    /// value converted to <typeparamref name="TResult"/> by these same rules, so nested levels flatten to
    /// one: <c>Some (Some x)</c> is <c>x</c>, while <c>Some None</c> and <c>None</c> are both
    /// <c>default(TResult)</c>. A <see cref="DamlOptionalChain"/> is never flattened
    /// into a <see cref="DamlValue"/>-derived target, which would lose its levels.</item>
    /// <item><see cref="DamlRecord"/> → a generated record type, through its
    /// <see cref="IDamlRecord{TSelf}.FromRecord(DamlRecord)"/> factory. A failure inside the factory
    /// surfaces as itself, not wrapped.</item>
    /// </list>
    /// Any other combination throws <see cref="NotSupportedException"/>.
    /// <para>
    /// The assignability check runs before any unwrapping so that asking for
    /// <see cref="DamlUnit"/> or <see cref="DamlValue"/> itself returns the instance rather than
    /// <c>default</c>. Beyond that check a <see cref="Nullable{T}"/> target is treated as its
    /// underlying type — unboxing a boxed <c>T</c> to <c>T?</c> is a well-defined CLR conversion
    /// — and a nullable value type is a valid destination for <see cref="DamlUnit"/> because it
    /// can represent "no value".
    /// </para>
    /// </remarks>
    [return: MaybeNull]
    public static TResult FromDamlValue<TResult>(this DamlValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (typeof(TResult).IsAssignableFrom(value.GetType()))
            return (TResult)(object)value;

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);

        if (value is DamlUndecodedJson undecoded)
            return UndecodedJsonReader.Read(undecoded, targetType).FromDamlValue<TResult>();

        if (value is DamlUnit)
        {
            if (typeof(TResult).IsValueType && Nullable.GetUnderlyingType(typeof(TResult)) is null)
                throw new NotSupportedException(
                    $"Cannot convert DamlUnit to value type {typeof(TResult)}. " +
                    $"Unit represents 'no value' and has no meaningful conversion to {typeof(TResult)}.");
            return default!;
        }

        if (value is (DamlOptional or DamlOptionalChain) && IsStdlibOptional(targetType))
            return (TResult)InvokeUnwrapped(StdlibOptionalFactory(targetType), value)!;

        if (value is DamlOptional || (value is DamlOptionalChain && !IsDamlValueType(typeof(TResult))))
            return FromOptional<TResult>(value.AsOptional());

        if (targetType == typeof(string))
        {
            return value switch
            {
                DamlText text => (TResult)(object)text.Value,
                DamlParty party => (TResult)(object)party.Value,
                DamlContractId contractId => (TResult)(object)contractId.Value,
                _ => throw new NotSupportedException(
                    $"Cannot convert {value.GetType()} to string. " +
                    $"Only DamlText, DamlParty, and DamlContractId can be unwrapped to string.")
            };
        }

        if (targetType == typeof(long) && value is DamlInt64 i64)
            return (TResult)(object)i64.Value;

        if (targetType == typeof(bool) && value is DamlBool b)
            return (TResult)(object)b.Value;

        if (targetType == typeof(decimal) && value is DamlNumeric n)
            return (TResult)(object)n.Value;

        if (targetType == typeof(DateOnly) && value is DamlDate d)
            return (TResult)(object)d.Value;

        if (targetType == typeof(DateTimeOffset) && value is DamlTimestamp ts)
            return (TResult)(object)ts.Value;

        if (targetType == typeof(Party) && value is DamlParty p)
            return (TResult)(object)Party.FromDamlValue(p);

        if (value is DamlContractId cid && targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(ContractId<>))
        {
            var instance = Activator.CreateInstance(targetType, cid.Value)
                ?? throw new InvalidOperationException(
                    $"Failed to create {targetType} from contract ID '{cid.Value}'. " +
                    $"Ensure {targetType} has a public constructor accepting a string.");
            return (TResult)instance;
        }

        if (value is DamlRecord record && RecordFactoryOf(targetType) is { } recordFactory)
            return (TResult)InvokeUnwrapped(recordFactory, record)!;

        throw new NotSupportedException(
            $"Cannot convert {value.GetType()} to {typeof(TResult)}. " +
            $"Use a DamlValue-derived type as TResult for direct access.");
    }

    private static readonly MethodInfo FromDamlValueMethod =
        typeof(DamlValueExtensions).GetMethod(nameof(FromDamlValue))!;

    [return: MaybeNull]
    private static TResult FromOptional<TResult>(DamlOptional optional)
    {
        if (optional.Value is null)
        {
            if (typeof(TResult).IsValueType && Nullable.GetUnderlyingType(typeof(TResult)) is null)
                throw new NotSupportedException(
                    $"Cannot convert an empty DamlOptional to value type {typeof(TResult)}. " +
                    $"None represents 'no value' and has no meaningful conversion to {typeof(TResult)}; use {typeof(TResult)}? instead.");
            return default!;
        }

        return (TResult)InvokeUnwrapped(FromDamlValueMethod.MakeGenericMethod(typeof(TResult)), optional.Value)!;
    }

    private static bool IsDamlValueType(Type type) => typeof(DamlValue).IsAssignableFrom(type);

    private static bool IsStdlibOptional(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);

    private static MethodInfo StdlibOptionalFactory(Type optionalType) =>
        StdlibOptionalMethod.MakeGenericMethod(optionalType.GetGenericArguments()[0]);

    private static readonly MethodInfo StdlibOptionalMethod =
        typeof(DamlValueExtensions).GetMethod(nameof(ToStdlibOptional), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Optional<T> ToStdlibOptional<T>(DamlValue value)
        where T : notnull =>
        Optional<T>.FromValue(value, static carried => carried.FromDamlValue<T>()!);

    private static MethodInfo? RecordFactoryOf(Type recordType)
    {
        var implementsRecordFactory = recordType.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(IDamlRecord<>)
            && contract.GetGenericArguments()[0] == recordType);

        return implementsRecordFactory
            ? recordType.GetMethod(nameof(IDamlRecord<>.FromRecord), BindingFlags.Public | BindingFlags.Static, [typeof(DamlRecord)])
            : null;
    }

    private static object? InvokeUnwrapped(MethodInfo method, DamlValue argument) =>
        method.Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, [argument], culture: null);
}
