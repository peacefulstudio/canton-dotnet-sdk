// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Codegen.CSharp.Cli.Versioning;

/// <summary>
/// File-backed store mapping each codegen-tool version to a generation ordinal —
/// the 4th NuGet version segment. Every package produced by a given codegen
/// version, within one source's store, shares the same ordinal; the ordinal
/// increments when the codegen version changes, independent of DAR content.
/// </summary>
/// <remarks>
/// <para><b>Single-writer precondition.</b> Instances are not thread-safe and the
/// on-disk file uses no cross-process locking. Callers must serialize access to a
/// given store path — both across threads in one process and across processes. The
/// release pipeline that owns the store path satisfies this naturally: it runs as a
/// single job, sequentially per package.</para>
/// <para><b>Atomic on-disk update.</b> Each minting <see cref="ResolveGeneration"/>
/// write goes via a sibling <c>.tmp</c> file and an atomic
/// <see cref="File.Move(string, string, bool)"/>, so a crash mid-write leaves the
/// previous valid file intact rather than truncating it to empty.</para>
/// <para><b>Legacy migration.</b> A live old-shape store keyed by
/// <c>{packageName}@{Major.Minor.Patch}</c> with per-entry <c>content_hash</c> and
/// <c>revision</c> values is validated strictly: the highest legacy revision seeds the
/// high-water mark, so the first minted ordinal is strictly greater than any published
/// revision. The first mint rewrites the file in the new shape, dropping the legacy
/// entries — safe because the recorded ordinal preserves the floor. A malformed legacy
/// entry is rejected rather than skipped, so a store of only-corrupt entries cannot
/// silently seed a below-published floor and republish an already-taken version. A
/// legacy store predates the <c>store_version</c> marker entirely (see below) and is not
/// required to carry one.</para>
/// <para><b>Schema-version marker.</b> Every store this code persists stamps a
/// top-level <c>store_version</c> field (currently <c>1</c>) alongside
/// <c>codegen_generations</c>. On load, a store that carries a <c>codegen_generations</c>
/// field is treated as "new-shape" and must also carry a recognized <c>store_version</c>;
/// a missing or unrecognized marker on such a store throws rather than silently resolving
/// as fresh, because a genuinely fresh or legacy-only store can never reach this branch
/// (see <see cref="OpenOrCreate"/>). This closes the gap where a misspelled, moved, or
/// wrong-shape <c>codegen_generations</c> field was indistinguishable from a legitimately
/// empty store.</para>
/// </remarks>
internal sealed class JsonReleaseCounterStore
{
    private const string GenerationsField = "codegen_generations";
    private const string StoreVersionField = "store_version";
    private const string LegacyContentHashField = "content_hash";
    private const string LegacyRevisionField = "revision";
    private const string RecoveryGuidance =
        "Repair the file or restore it from its last good state; do not reset it to an empty table, which re-zeros every recorded ordinal and can republish an already-taken version.";
    private const int CurrentStoreVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Dictionary<string, int> _generations;
    private readonly int _legacyHighWater;

    private JsonReleaseCounterStore(string path, Dictionary<string, int> generations, int legacyHighWater)
    {
        _path = path;
        _generations = generations;
        _legacyHighWater = legacyHighWater;
    }

    /// <summary>
    /// Opens an existing JSON store at <paramref name="path"/>, or returns an empty
    /// in-memory store that will be persisted on the first minting
    /// <see cref="ResolveGeneration"/> call if the file does not yet exist.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file exists but is empty or whitespace-only, does not parse as a
    /// JSON object, carries a malformed <c>codegen_generations</c> map, carries a
    /// malformed legacy entry (a non-object value, a missing or non-string
    /// <c>content_hash</c>, or a missing, non-integer, or negative <c>revision</c>), or
    /// carries a <c>codegen_generations</c> field without a matching top-level
    /// <c>store_version</c> marker (missing entirely, or set to a value this build does
    /// not recognize), or carries a top-level <c>store_version</c> marker without a
    /// <c>codegen_generations</c> field. A truncated or zero-byte store is treated as corruption rather
    /// than a fresh start: minting from an empty table would re-zero every recorded
    /// ordinal and could republish an already-taken version. A valid empty JSON object
    /// (<c>{}</c>) is not corruption — it resolves as an empty store, preserving the
    /// first-run bootstrap, and is not required to carry a <c>store_version</c> (nothing
    /// written by this code is ever lost by treating it as fresh). A legacy-only store
    /// (no <c>codegen_generations</c> field) likewise is not required to carry a
    /// <c>store_version</c>, since it predates the marker. The exception names the
    /// offending path so the failure is diagnosable mid-CI-run. Recovery is a human
    /// decision, so this never falls back to an empty store on a load failure.
    /// </exception>
    public static JsonReleaseCounterStore OpenOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return Empty(path);
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' is empty or whitespace-only. {RecoveryGuidance}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException inner)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' is not valid JSON (line {inner.LineNumber}, position {inner.BytePositionInLine}). {RecoveryGuidance}",
                inner);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' must have a JSON object at its root. {RecoveryGuidance}");
            }

            var generations = new Dictionary<string, int>(StringComparer.Ordinal);
            var legacyHighWater = -1;
            var hasGenerationsField = false;
            JsonElement? storeVersionElement = null;

            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, StoreVersionField, StringComparison.Ordinal))
                {
                    storeVersionElement = property.Value;
                    continue;
                }

                if (string.Equals(property.Name, GenerationsField, StringComparison.Ordinal))
                {
                    hasGenerationsField = true;
                    ReadGenerations(property.Value, path, generations);
                    continue;
                }

                legacyHighWater = Math.Max(legacyHighWater, ReadLegacyRevision(property.Value, property.Name, path));
            }

            ValidateSchemaMarker(hasGenerationsField, storeVersionElement, path);

            return new JsonReleaseCounterStore(path, generations, legacyHighWater);
        }
    }

    /// <summary>
    /// Resolves the generation ordinal for <paramref name="codegenVersion"/>.
    /// A version already recorded returns its ordinal unchanged (idempotent, no
    /// write). An unseen version mints <c>highWater + 1</c> — where <c>highWater</c>
    /// spans every recorded ordinal and every migrated legacy revision, or <c>-1</c>
    /// for a completely empty store — records it, persists, and returns it.
    /// </summary>
    public int ResolveGeneration(string codegenVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codegenVersion);

        if (_generations.TryGetValue(codegenVersion, out var existing))
        {
            return existing;
        }

        var ordinal = HighWater() + 1;
        _generations[codegenVersion] = ordinal;
        Persist();
        return ordinal;
    }

    private int HighWater()
    {
        var water = _legacyHighWater;
        foreach (var ordinal in _generations.Values)
        {
            water = Math.Max(water, ordinal);
        }
        return water;
    }

    private static JsonReleaseCounterStore Empty(string path) =>
        new(path, new Dictionary<string, int>(StringComparer.Ordinal), legacyHighWater: -1);

    private static void ValidateSchemaMarker(bool hasGenerationsField, JsonElement? storeVersionElement, string path)
    {
        if (!hasGenerationsField)
        {
            if (storeVersionElement is not null)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a top-level '{StoreVersionField}' field but no '{GenerationsField}' field. Every store this build writes stamps both; a '{StoreVersionField}' without '{GenerationsField}' means the file was hand-edited, truncated, or produced by incompatible code, and resolving it as a fresh store would re-zero every recorded ordinal. {RecoveryGuidance}");
            }

            return;
        }

        if (storeVersionElement is not { } element)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has a '{GenerationsField}' field but no top-level '{StoreVersionField}' field. Every store this build writes stamps '{StoreVersionField}': {CurrentStoreVersion}; a missing marker means the file was hand-edited, moved from elsewhere, or produced by incompatible code. {RecoveryGuidance}");
        }

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var storeVersion)
            || storeVersion != CurrentStoreVersion)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has an unrecognized '{StoreVersionField}' value ('{element.GetRawText()}'); this build only understands store_version {CurrentStoreVersion}. {RecoveryGuidance}");
        }
    }

    private static void ReadGenerations(JsonElement element, string path, Dictionary<string, int> generations)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has a '{GenerationsField}' field that is not a JSON object. {RecoveryGuidance}");
        }

        foreach (var generation in element.EnumerateObject())
        {
            if (generation.Value.ValueKind != JsonValueKind.Number
                || !generation.Value.TryGetInt32(out var ordinal)
                || ordinal < 0)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has an invalid generation ordinal for codegen version '{generation.Name}'; it must be a non-negative integer. {RecoveryGuidance}");
            }

            generations[generation.Name] = ordinal;
        }
    }

    private static int ReadLegacyRevision(JsonElement element, string key, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has a legacy entry for key '{key}' that is not a JSON object. {RecoveryGuidance}");
        }

        if (!element.TryGetProperty(LegacyContentHashField, out var contentHash)
            || contentHash.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(contentHash.GetString()))
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has a missing or empty content_hash for legacy entry '{key}'. {RecoveryGuidance}");
        }

        if (!element.TryGetProperty(LegacyRevisionField, out var revisionElement)
            || revisionElement.ValueKind != JsonValueKind.Number
            || !revisionElement.TryGetInt32(out var revision)
            || revision < 0)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' has a missing, non-integer, or negative revision for legacy entry '{key}'. {RecoveryGuidance}");
        }

        return revision;
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new StoreDocument(
            CurrentStoreVersion,
            new SortedDictionary<string, int>(_generations, StringComparer.Ordinal));
        var json = JsonSerializer.Serialize(document, SerializerOptions);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private sealed record StoreDocument(int StoreVersion, SortedDictionary<string, int> CodegenGenerations);
}
