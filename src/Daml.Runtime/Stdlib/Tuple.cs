// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.Types.Tuple2 a b</c> — a two-element heterogeneous tuple.
/// On the wire the type is a record with fields <c>_1 : a</c> and <c>_2 : b</c>.
/// </summary>
/// <remarks>
/// <para>
/// The C# codegen emits the type with concrete CLR generic arguments
/// (e.g. <c>Tuple2&lt;Party, long&gt;</c>) which are not in general
/// <see cref="IDamlRecord"/> — primitive types like <c>long</c> have no <c>ToRecord</c>
/// method. Round-tripping therefore goes through caller-supplied converters that
/// bridge the generic CLR type to <see cref="DamlValue"/>; the codegen knows the
/// concrete types at the call site and inlines the appropriate conversion lambdas.
/// </para>
/// <para>
/// Tuple2 is heavily used in Splice DARs (e.g. <c>splice-token-test-trading-app</c>,
/// <c>splice-wallet</c>, <c>splice-amulet</c>) and is part of the
/// <c>daml-prim-DA-Types</c> stable package, so it is hand-coded here rather than
/// regenerated per consumer.
/// </para>
/// </remarks>
/// <typeparam name="T1">Type of the first component.</typeparam>
/// <typeparam name="T2">Type of the second component.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Tuple2<...>.FromRecord.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "_1, _2 and _3 are the field names Daml-LF gives tuple components; renaming them would break the record round-trip the wire format is keyed on.")]
public sealed record Tuple2<T1, T2>(T1 _1, T2 _2)
    where T1 : notnull
    where T2 : notnull
{
    /// <summary>
    /// Converts this tuple to its Ledger API record representation. The supplied
    /// delegates encode each component to a <see cref="DamlValue"/>.
    /// </summary>
    public DamlRecord ToRecord(Func<T1, DamlValue> convert1, Func<T2, DamlValue> convert2)
    {
        ArgumentNullException.ThrowIfNull(convert1);
        ArgumentNullException.ThrowIfNull(convert2);
        return DamlRecord.Create(
            DamlField.Create("_1", convert1(_1)),
            DamlField.Create("_2", convert2(_2)));
    }

    /// <summary>
    /// Reconstructs a tuple from its Ledger API record representation. The supplied
    /// delegates decode each component from its <see cref="DamlValue"/> form. A component the record
    /// omits is handed to its converter as its <c>absentReadsAs</c>, which the call site gives when the
    /// component is a flat or a nested <c>Optional</c>, and is required when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="record">The record to read.</param>
    /// <param name="convert1">Decodes the first component.</param>
    /// <param name="absentReadsAs1">What an omitted first component reads as: <see cref="DamlOptional.None"/> for an <c>Optional</c>, <see langword="null"/> for a required component.</param>
    /// <param name="convert2">Decodes the second component.</param>
    /// <param name="absentReadsAs2">What an omitted second component reads as, by the same convention.</param>
    /// <exception cref="InvalidOperationException">The record omits a required component.</exception>
    public static Tuple2<T1, T2> FromRecord(
        DamlRecord record,
        Func<DamlValue, T1> convert1,
        DamlValue? absentReadsAs1,
        Func<DamlValue, T2> convert2,
        DamlValue? absentReadsAs2)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convert1);
        ArgumentNullException.ThrowIfNull(convert2);
        return new Tuple2<T1, T2>(
            record.GetTypeParameterField("_1", convert1, absentReadsAs1),
            record.GetTypeParameterField("_2", convert2, absentReadsAs2));
    }
}

/// <summary>
/// Daml stdlib type <c>DA.Types.Tuple3 a b c</c> — a three-element heterogeneous tuple.
/// On the wire the type is a record with fields <c>_1 : a</c>, <c>_2 : b</c>, <c>_3 : c</c>.
/// </summary>
/// <remarks>
/// See <see cref="Tuple2{T1, T2}"/> for the rationale behind the delegate-based
/// converter API.
/// </remarks>
/// <typeparam name="T1">Type of the first component.</typeparam>
/// <typeparam name="T2">Type of the second component.</typeparam>
/// <typeparam name="T3">Type of the third component.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Tuple3<...>.FromRecord.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "_1, _2 and _3 are the field names Daml-LF gives tuple components; renaming them would break the record round-trip the wire format is keyed on.")]
public sealed record Tuple3<T1, T2, T3>(T1 _1, T2 _2, T3 _3)
    where T1 : notnull
    where T2 : notnull
    where T3 : notnull
{
    /// <summary>
    /// Converts this tuple to its Ledger API record representation.
    /// </summary>
    public DamlRecord ToRecord(
        Func<T1, DamlValue> convert1,
        Func<T2, DamlValue> convert2,
        Func<T3, DamlValue> convert3)
    {
        ArgumentNullException.ThrowIfNull(convert1);
        ArgumentNullException.ThrowIfNull(convert2);
        ArgumentNullException.ThrowIfNull(convert3);
        return DamlRecord.Create(
            DamlField.Create("_1", convert1(_1)),
            DamlField.Create("_2", convert2(_2)),
            DamlField.Create("_3", convert3(_3)));
    }

    /// <summary>
    /// Reconstructs a tuple from its Ledger API record representation. A component the record omits is
    /// handed to its converter as its <c>absentReadsAs</c>, which the call site gives when the
    /// component is a flat or a nested <c>Optional</c>, and is required when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="record">The record to read.</param>
    /// <param name="convert1">Decodes the first component.</param>
    /// <param name="absentReadsAs1">What an omitted first component reads as: <see cref="DamlOptional.None"/> for an <c>Optional</c>, <see langword="null"/> for a required component.</param>
    /// <param name="convert2">Decodes the second component.</param>
    /// <param name="absentReadsAs2">What an omitted second component reads as, by the same convention.</param>
    /// <param name="convert3">Decodes the third component.</param>
    /// <param name="absentReadsAs3">What an omitted third component reads as, by the same convention.</param>
    /// <exception cref="InvalidOperationException">The record omits a required component.</exception>
    public static Tuple3<T1, T2, T3> FromRecord(
        DamlRecord record,
        Func<DamlValue, T1> convert1,
        DamlValue? absentReadsAs1,
        Func<DamlValue, T2> convert2,
        DamlValue? absentReadsAs2,
        Func<DamlValue, T3> convert3,
        DamlValue? absentReadsAs3)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convert1);
        ArgumentNullException.ThrowIfNull(convert2);
        ArgumentNullException.ThrowIfNull(convert3);
        return new Tuple3<T1, T2, T3>(
            record.GetTypeParameterField("_1", convert1, absentReadsAs1),
            record.GetTypeParameterField("_2", convert2, absentReadsAs2),
            record.GetTypeParameterField("_3", convert3, absentReadsAs3));
    }
}
