// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// A contract explicitly disclosed alongside a submission so that a party
/// without native visibility into it can still be authorized to act on it —
/// Daml 3.x explicit disclosure. The Canton ledger client owns mapping this
/// onto the gRPC <c>DisclosedContract</c> message; this type is transport-neutral.
/// </summary>
/// <param name="ContractId">The disclosed contract's identifier.</param>
/// <param name="TemplateId">The disclosed contract's template identifier.</param>
/// <param name="CreatedEventBlob">
/// The raw <c>created_event_blob</c> bytes from the gRPC <c>CreatedEvent</c>,
/// carried verbatim and opaque to this library — no encoding is imposed on callers.
/// Copied into this record, which then owns its bytes.
/// </param>
public sealed record DisclosedContract(
    string ContractId,
    Identifier TemplateId,
    ReadOnlyMemory<byte> CreatedEventBlob)
{
    private readonly ReadOnlyMemory<byte> _createdEventBlob = CreatedEventBlob.ToArray();

    /// <summary>
    /// The raw <c>created_event_blob</c> bytes carried by this disclosed contract. Copied
    /// at construction and on <c>init</c>, so a caller that retains the array it supplied
    /// cannot change the disclosed payload, this value's equality, or its hash code after
    /// the contract has been attached to a submission.
    /// </summary>
    public ReadOnlyMemory<byte> CreatedEventBlob
    {
        get => _createdEventBlob;
        init => _createdEventBlob = value.ToArray();
    }

    /// <summary>
    /// The synchronizer the disclosed contract is assigned to, so a participant connected to
    /// several synchronizers routes the submission to the one that holds it. <c>null</c> leaves
    /// the choice to the participant.
    /// </summary>
    public SynchronizerId? SynchronizerId { get; init; }

    /// <summary>
    /// Compares <see cref="CreatedEventBlob"/> byte-for-byte, unlike the synthesized
    /// record equality, which compares only the memory segment's reference, offset,
    /// and length.
    /// </summary>
    public bool Equals(DisclosedContract? other) =>
        other is not null
        && ContractId == other.ContractId
        && TemplateId == other.TemplateId
        && SynchronizerId == other.SynchronizerId
        && CreatedEventBlob.Span.SequenceEqual(other.CreatedEventBlob.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractId);
        hash.Add(TemplateId);
        hash.Add(SynchronizerId);
        hash.AddBytes(CreatedEventBlob.Span);
        return hash.ToHashCode();
    }
}
