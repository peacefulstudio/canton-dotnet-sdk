// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Canton.Ledger.Kernel.Wire;

/// <summary>
/// What a <c>createdEventBlob</c> says about the contract it discloses, as read by
/// <see cref="CreatedEventBlob.Decode"/>.
/// </summary>
/// <remarks>
/// The blob carries no interface view: a participant computes a view when it serves a created event for
/// an interface filter, and the blob a disclosure hands out is the contract instance alone.
/// </remarks>
public sealed class DecodedCreatedEventBlob
{
    private readonly byte[]? _contractKey;
    private readonly ReadOnlyMemory<byte> _contractKeyHash;

    internal DecodedCreatedEventBlob(
        string serializationVersion,
        string contractId,
        string packageName,
        Identifier templateId,
        IReadOnlySet<string> signatories,
        IReadOnlySet<string> observers,
        IReadOnlySet<string> keyMaintainers,
        DateTimeOffset? createdAt,
        ReadOnlyMemory<byte> createArgument,
        byte[]? contractKey,
        ReadOnlyMemory<byte> contractKeyHash)
    {
        SerializationVersion = serializationVersion;
        ContractId = contractId;
        PackageName = packageName;
        TemplateId = templateId;
        Signatories = signatories;
        Observers = observers;
        KeyMaintainers = keyMaintainers;
        CreatedAt = createdAt;
        CreateArgument = createArgument;
        _contractKey = contractKey;
        _contractKeyHash = contractKeyHash;
    }

    /// <summary>The Daml-LF serialization version the blob is encoded in, such as <c>2.1</c>.</summary>
    public string SerializationVersion { get; }

    /// <summary>The contract id in the lower-case hexadecimal form the Ledger API uses.</summary>
    public string ContractId { get; }

    /// <summary>The package name of the template's package.</summary>
    public string PackageName { get; }

    /// <summary>
    /// The template, addressed by the package id the contract was created under; the module and entity
    /// names are the dotted names Daml-LF stores as segments.
    /// </summary>
    public Identifier TemplateId { get; }

    /// <summary>
    /// Every signatory of the contract: the non-maintainer signatories together with the contract key's
    /// maintainers, which are signatories too.
    /// </summary>
    public IReadOnlySet<string> Signatories { get; }

    /// <summary>The stakeholders that are not signatories.</summary>
    public IReadOnlySet<string> Observers { get; }

    /// <summary>The maintainers of the contract key; empty when the template has no key.</summary>
    public IReadOnlySet<string> KeyMaintainers { get; }

    /// <summary>
    /// When the contract was created, or <c>null</c> when the blob records the creation time as unknown.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// The contract's create argument exactly as the blob carries it: a serialized Daml-LF
    /// <c>Value</c>. <see cref="ReadCreateArgument"/> and <see cref="ReadContract{T}"/> interpret it.
    /// </summary>
    public ReadOnlyMemory<byte> CreateArgument { get; }

    /// <summary>
    /// Reads the create argument without a generated template type: the untyped fallback for a template
    /// this app holds no binding for.
    /// </summary>
    /// <remarks>
    /// Daml-LF values carry no field labels and no record, variant or enum ids, so the record's fields are
    /// positional: every <see cref="DamlField.Label"/> is empty and <see cref="DamlRecord.RecordId"/> is
    /// <c>null</c>. A generated template reads such a record by field position.
    /// </remarks>
    /// <returns>The create argument as a <see cref="DamlRecord"/>.</returns>
    /// <exception cref="MalformedResponseException">
    /// The create argument is not a readable Daml-LF value, or is not a record.
    /// </exception>
    public DamlRecord ReadCreateArgument() =>
        LfValueReader.Read(CreateArgument) as DamlRecord
            ?? throw new MalformedResponseException("the createdEventBlob's create argument is not a record.");

    /// <summary>
    /// Reads the contract key without a generated key type, positionally like
    /// <see cref="ReadCreateArgument"/>.
    /// </summary>
    /// <returns>The contract key, or <c>null</c> when the template has no key.</returns>
    /// <exception cref="MalformedResponseException">The key is not a readable Daml-LF value.</exception>
    public DamlValue? ReadContractKey() =>
        _contractKey is null ? null : LfValueReader.Read(_contractKey);

    /// <summary>
    /// Reads the contract as the generated template <typeparamref name="T"/>, which may or may not declare
    /// a contract key; the key is not read.
    /// </summary>
    /// <remarks>
    /// The blob must be of <typeparamref name="T"/>'s package name, module and entity. Its package id may
    /// differ from <typeparamref name="T"/>'s: a contract created under an earlier version of the package is
    /// read through the version the binding was generated from.
    /// </remarks>
    /// <typeparam name="T">The generated template type.</typeparam>
    /// <returns>The contract id and payload.</returns>
    /// <exception cref="InvalidOperationException">The blob is of another template than <typeparamref name="T"/>.</exception>
    /// <exception cref="MalformedResponseException">The create argument cannot be read as <typeparamref name="T"/>.</exception>
    public Contract<T> ReadContract<T>()
        where T : ITemplate, IDamlRecord<T>
    {
        RequireTemplate<T>();
        return new Contract<T>(new ContractId<T>(ContractId), ReadPayload<T>());
    }

    /// <summary>
    /// Reads the contract as the generated keyed template <typeparamref name="T"/>, with its contract key
    /// decoded as <typeparamref name="TKey"/> and the key's hash as the blob records it.
    /// </summary>
    /// <remarks>
    /// The same template match as <see cref="ReadContract{T}"/> applies. The key's hash is the base64 text
    /// <see cref="ContractKey{TKey}.Hash"/> documents, or <c>null</c> when the blob records none.
    /// </remarks>
    /// <typeparam name="T">The generated template type.</typeparam>
    /// <typeparam name="TKey">The template's contract key type.</typeparam>
    /// <returns>The contract id, payload and key.</returns>
    /// <exception cref="InvalidOperationException">The blob is of another template than <typeparamref name="T"/>.</exception>
    /// <exception cref="MalformedResponseException">
    /// The blob carries no contract key, or the create argument or the key cannot be read as
    /// <typeparamref name="T"/> and <typeparamref name="TKey"/>.
    /// </exception>
    public Contract<T, TKey> ReadContract<T, TKey>()
        where T : ITemplate, IDamlRecord<T>, IHasKey<T, TKey>
    {
        RequireTemplate<T>();
        var payload = ReadPayload<T>();
        var key = ReadContractKey()
            ?? throw new MalformedResponseException(
                "the createdEventBlob's contract instance carries no contract key, though the template is keyed.");
        var decodedKey = MalformedResponse.Decoding(key, T.Key.KeyDecoder);
        var hash = _contractKeyHash.IsEmpty ? null : Convert.ToBase64String(_contractKeyHash.Span);
        return new Contract<T, TKey>(new ContractId<T>(ContractId), payload, new ContractKey<TKey>(decodedKey, hash));
    }

    private T ReadPayload<T>()
        where T : IDamlRecord<T> =>
        MalformedResponse.Decoding(ReadCreateArgument(), T.FromRecord);

    private void RequireTemplate<T>()
        where T : ITemplate
    {
        var expected = T.TemplateId;
        if (PackageName != T.PackageName || TemplateId.ModuleName != expected.ModuleName || TemplateId.EntityName != expected.EntityName)
        {
            throw new InvalidOperationException(
                $"the createdEventBlob is a {PackageName}:{TemplateId.ModuleName}:{TemplateId.EntityName} contract, "
                + $"not a {T.PackageName}:{expected.ModuleName}:{expected.EntityName} one.");
        }
    }
}
