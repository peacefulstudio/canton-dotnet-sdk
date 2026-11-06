// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// A top-level Daml value whose whole body is a single literal.
/// </summary>
/// <param name="Name">The Daml-LF value name, mangled as the archive spells it.</param>
/// <param name="Value">The literal the value is defined as.</param>
public sealed record DamlConstant(string Name, DamlConstantValue Value);

/// <summary>
/// Base class for the literal kinds a <see cref="DamlConstant"/> can carry.
/// </summary>
public abstract record DamlConstantValue;

/// <summary>
/// A Daml <c>Text</c> literal.
/// </summary>
public sealed record DamlTextConstantValue(string Value) : DamlConstantValue;

/// <summary>
/// A Daml <c>Int</c> literal.
/// </summary>
public sealed record DamlInt64ConstantValue(long Value) : DamlConstantValue;

/// <summary>
/// A Daml <c>Bool</c> literal.
/// </summary>
public sealed record DamlBoolConstantValue(bool Value) : DamlConstantValue;

/// <summary>
/// A Daml <c>Numeric</c> literal, in plain decimal notation as the archive spells it: an optional
/// leading <c>-</c>, integer digits, then <c>.</c> and fraction digits.
/// </summary>
public sealed record DamlNumericConstantValue(string Value) : DamlConstantValue;
