// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Wire property name of the <see cref="Raw.Value"/> sum that our own specification does not
/// declare as an arm, so it travels through <c>AdditionalProperties</c> instead. Encoder, decoder
/// and Daml-LF writer share this name, so a rename fails the build rather than silently leaving
/// one of them looking for a key nothing writes.
/// </summary>
internal static class WireValueNames
{
    /// <summary>
    /// Holds the raw Daml-LF JSON text of every wire <see cref="Raw.Value"/> and <see cref="Raw.Record"/>
    /// read from a participant — ambiguous without the Daml type, so the read path preserves it here
    /// for a type-directed decode instead of binding it to an arm.
    /// </summary>
    internal const string Idiomatic = "idiomatic";
}
