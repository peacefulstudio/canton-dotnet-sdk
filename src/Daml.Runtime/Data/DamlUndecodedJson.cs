// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// A Daml value read from a participant over the JSON Ledger API whose type has no generated binding in the
/// process, carried as the Daml-LF JSON the participant sent. A client projects an event it cannot decode into
/// this value instead of failing the transaction that holds it; decode it later through the generated type's
/// own reader, for example with <see cref="DamlValueExtensions.FromDamlValue{TResult}"/> once the binding is loaded.
/// </summary>
/// <param name="LfJson">The Daml-LF JSON text of the value, exactly as the participant sent it.</param>
public sealed record DamlUndecodedJson(string LfJson) : DamlValue;
