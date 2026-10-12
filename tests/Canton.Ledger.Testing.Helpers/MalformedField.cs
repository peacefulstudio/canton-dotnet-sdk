// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Canton.Ledger.Testing.Helpers;

/// <summary>
/// The part of a participant's response that is wrong, named in no transport's vocabulary. Each
/// transport's malformed-response sweep builds the matching wire body in its own encoding, so both
/// are held to the same verdict for the same fault.
/// </summary>
public enum MalformedField
{
    /// <summary>A created event carrying the empty string as its contract id.</summary>
    ContractIdEmpty,

    /// <summary>A created event carrying nothing but whitespace as its contract id.</summary>
    ContractIdWhitespace,

    /// <summary>A created event that omits its contract id altogether.</summary>
    ContractIdMissing,

    /// <summary>A created event that omits its template id altogether.</summary>
    TemplateIdMissing,

    /// <summary>An archived event carrying the empty string as its contract id.</summary>
    ArchivedContractIdEmpty,

    /// <summary>An offset below zero, which no <c>LedgerOffset</c> can carry.</summary>
    OffsetNegative,

    /// <summary>An archived event whose offset is below zero.</summary>
    ArchivedOffsetNegative,

    /// <summary>An offset the wire encodes as text that is not a number.</summary>
    OffsetNonNumeric,

    /// <summary>An exercised event acting as the empty party, which no <c>Party</c> can carry.</summary>
    ActingPartyEmpty,

    /// <summary>
    /// A command id of nothing but whitespace, which no <c>CommandId</c> can carry. Distinct from an
    /// absent command id, which both transports read as no command id.
    /// </summary>
    CommandIdWhitespace,

    /// <summary>The response omits the one value the call exists to return.</summary>
    ResultMissing,

    /// <summary>The response omits the synchronizer id it is documented to carry.</summary>
    SynchronizerIdMissing,

    /// <summary>A create argument that lacks a field its template requires.</summary>
    CreateArgumentFieldMissing,

    /// <summary>A create argument whose field holds a value of another Daml kind than its template declares.</summary>
    CreateArgumentWrongShape,

    /// <summary>A timestamp outside the range a <c>DateTimeOffset</c> can carry.</summary>
    TimestampOutOfRange,

    /// <summary>A traffic cost outside the range its signed integer type can carry.</summary>
    CostOutOfRange,

    /// <summary>An enum value this SDK version does not name, such as hashing scheme version 99.</summary>
    EnumValueUnknown,

    /// <summary>A success body that is not a readable document at all.</summary>
    BodyUnreadable,

    /// <summary>A party detail carrying the empty string as its party, which no <c>Party</c> can carry.</summary>
    PartyEmpty,

    /// <summary>A user whose primary party is nothing but whitespace, which no <c>Party</c> can carry.</summary>
    PrimaryPartyWhitespace,

    /// <summary>A vetted-packages group carrying the empty string as its synchronizer id, which no <c>SynchronizerId</c> can carry.</summary>
    SynchronizerIdEmpty,

    /// <summary>A <c>oneof</c> whose set arm is one this client does not know, such as a user right newer than the vendored surface.</summary>
    UnknownOneofArm,
}
