// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Rest.Client;

/// <summary>
/// Wire property names of the <see cref="Raw.Value"/> sum that our own specification does not
/// declare as arms, so they travel through <c>AdditionalProperties</c> instead. Encoder, decoder
/// and Daml-LF writer share these names, so a rename fails the build rather than silently leaving
/// one of them looking for a key nothing writes.
/// </summary>
internal static class WireValueNames
{
    internal const string Unit = "unit";

    /// <summary>
    /// Marks a wire <see cref="Raw.Optional"/> as one level of a nested <c>Optional (Optional a)</c>
    /// chain. The wire shape cannot tell a chain level from a flat <c>Optional a</c>, yet Daml-LF JSON
    /// writes every chain level as an array and a flat one as <c>null</c> or its bare value.
    /// </summary>
    internal const string OptionalChain = "optionalChain";

    /// <summary>
    /// Holds the raw Daml-LF JSON text of every wire <see cref="Raw.Value"/> and <see cref="Raw.Record"/>
    /// read from a participant — ambiguous without the Daml type, so the read path preserves it here
    /// for a type-directed decode instead of binding it to an arm.
    /// </summary>
    internal const string Idiomatic = "idiomatic";
}
