// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Implemented by the <see cref="JsonConverterFactory"/> for each of the runtime's
/// abstract-record discriminated unions, purely so <see cref="DiscriminatedUnionJson"/> can find
/// and strip them back out of a copy of the caller's <see cref="JsonSerializerOptions"/> — see
/// <see cref="DiscriminatedUnionJson"/>'s remarks — without naming each factory type individually.
/// </summary>
internal interface IDiscriminatedUnionJsonConverterFactory;

/// <summary>
/// Shared read/write logic for the <see cref="System.Text.Json"/> converters of the runtime's
/// abstract-record discriminated unions — <see cref="Daml.Runtime.Stdlib.Optional{T}"/>,
/// <see cref="Daml.Runtime.Stdlib.Either{TL, TR}"/>,
/// <see cref="Daml.Runtime.Streams.ContractStreamEvent{T}"/> and
/// <see cref="Daml.Runtime.Streams.InterfaceStreamEvent{TInterface, TView}"/>.
/// </summary>
/// <remarks>
/// Each union is written as its concrete arm's own JSON object, with a <c>"$case"</c>
/// discriminator naming the arm inserted as the first member, e.g.
/// <c>{"$case":"Some","Value":"c"}</c>. Reading looks the discriminator up in the caller-supplied
/// case map, strips the <c>"$case"</c> member, and deserializes the rest of the object at that
/// arm's concrete CLR type — stripped, rather than left for the arm's converter to see, because a
/// caller running under <see cref="System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow"/>
/// would otherwise reject every read of every arm on the one member none of them declare. A
/// payload nested inside another instance of the same union —
/// <c>Optional&lt;Optional&lt;T&gt;&gt;</c> — is unambiguous: each level carries its own
/// discriminator. This is a CLR round-trip contract, not the Daml-LF wire encoding; a generated
/// record's own field still reads and writes under its C# member names via
/// <see cref="Daml.Runtime.Serialization.DamlLfJsonReader"/> for the ledger, unrelated to this
/// shape.
/// </remarks>
/// <remarks>
/// <para>
/// <see cref="Read{TUnion}"/> parses each arm's JSON subtree via
/// <c>JsonDocument.ParseValue(ref Utf8JsonReader)</c>, which continues the ambient
/// <see cref="Utf8JsonReader"/>'s own token-depth tracking rather than starting fresh — so a union
/// nested inside itself on the wire is already caught by <see cref="System.Text.Json"/>'s own reader
/// depth guard (threaded from <see cref="JsonSerializerOptions.MaxDepth"/>) before any of this type's
/// code runs, with System.Text.Json's own <see cref="JsonException"/>. No extra guard is needed here.
/// </para>
/// <para>
/// Neither <see cref="Read{TUnion}"/> nor <see cref="Write{TUnion}"/> supports a
/// <see cref="JsonSerializerOptions.ReferenceHandler"/>: both reach the arm's payload through a
/// nested <see cref="JsonSerializer"/> call — <c>JsonNode.Deserialize</c> on read,
/// <c>JsonSerializer.SerializeToNode</c> on write — and each such call starts its own independent
/// reference resolver rather than continuing the ambient one the outer
/// <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/>/<c>Deserialize</c>
/// call is tracking. Under <see cref="System.Text.Json.Serialization.ReferenceHandler.Preserve"/> this
/// silently duplicates an aliased object instead of writing a <c>"$ref"</c> to it, with the duplicate
/// carrying a <c>"$id"</c> that can collide with one the outer scope already assigned; under
/// <see cref="System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles"/> a genuine cycle through one of these types is never
/// recognized as a cycle, so it recurses until <see cref="Write{TUnion}"/>'s own nesting-depth guard
/// above throws a <see cref="JsonException"/> that reads as a nesting-depth problem rather than the
/// reference cycle it actually is. Both methods therefore refuse outright, with a
/// <see cref="JsonException"/> naming the union type, whenever
/// <see cref="JsonSerializerOptions.ReferenceHandler"/> is set — the same fail-fast posture as an
/// unrecognized <c>"$case"</c> — rather than let either failure mode through silently or misleadingly.
/// </para>
/// <para>
/// <see cref="Write{TUnion}"/> is different: it serializes each arm via
/// <c>JsonSerializer.SerializeToNode(object?, Type, JsonSerializerOptions?)</c>, which starts a fresh,
/// independent serialization rather than continuing the ambient one, so a union nested inside itself
/// re-enters this type at CLR-call-stack cost for every level, and
/// <see cref="JsonSerializerOptions.MaxDepth"/> is only checked once the arm's whole node tree has
/// already been built in memory — a build deep enough to be dangerous can already have overrun the
/// stack before that check ever runs. <see cref="Write{TUnion}"/> therefore tracks its own effective
/// nesting depth — the ambient <see cref="Utf8JsonWriter.CurrentDepth"/> at the point each arm is
/// about to be written, plus a thread-static baseline carried across the nested
/// <c>SerializeToNode</c> calls that separate one union from the next — rather than counting only
/// how many times <see cref="Write{TUnion}"/> itself has re-entered. Ordinary record layers written
/// between two unions still cost real call-stack depth through System.Text.Json's own recursive
/// writer even though they never re-enter this method, so a guard that counted only union-to-union
/// hops could stay far below <see cref="JsonSerializerOptions.MaxDepth"/> while the real, ordinary-
/// layers-included depth had already reached it. <see cref="Write{TUnion}"/> fails with a
/// <see cref="JsonException"/> once this effective depth reaches the caller's own
/// <see cref="JsonSerializerOptions.MaxDepth"/> — or <see cref="DefaultMaxDepth"/> when unset,
/// mirroring <see cref="JsonSerializerOptions.MaxDepth"/>'s own default of 64. The real native
/// call-stack cost of each counted level is itself platform- and JIT-dependent — the same counted
/// depth can leave a different amount of real stack headroom on different targets — so
/// <see cref="Write{TUnion}"/> also fails the same way, before recursing further, whenever
/// <see cref="RuntimeHelpers.TryEnsureSufficientExecutionStack"/> reports the remaining call stack
/// is running low, even short of the counted depth above, rather than let a platform's own stack
/// budget be what decides whether this throws a <see cref="JsonException"/> or crashes the process.
/// </para>
/// <para>
/// Neither of those alone was enough in practice: a macos-amd64 CI run of exactly this path
/// (<c>daml-codegen-csharp</c> workflow run 35363419884, job 105660005803) crashed the test host
/// with an uncaught native stack overflow — process exit code 7, not the intended
/// <see cref="JsonException"/> — before either the counter or
/// <see cref="RuntimeHelpers.TryEnsureSufficientExecutionStack"/> tripped. Each counted level
/// re-enters <see cref="JsonSerializer"/> through roughly eight native frames (the
/// <c>Optional</c> wrapper → its <see cref="JsonConverter{T}"/>.TryWrite → the object converter →
/// <c>JsonTypeInfo</c>.Serialize → <c>WriteNodeAsObject</c>), and that platform's real per-level
/// cost through this chain outran both guards' margins well before the counted default of 64.
/// <see cref="Write{TUnion}"/> therefore also enforces <see cref="MaxSafeWriteDepth"/>: a fixed,
/// platform-independent ceiling chosen with real margin below <see cref="DefaultMaxDepth"/>. The
/// depth actually enforced is <c>Math.Min(callerOrDefaultMaxDepth, MaxSafeWriteDepth)</c>, so a
/// caller-configured <see cref="JsonSerializerOptions.MaxDepth"/> smaller than
/// <see cref="MaxSafeWriteDepth"/> is still honoured exactly as before, but one larger than it no
/// longer opts into recursion deeper than <see cref="MaxSafeWriteDepth"/> on this write path — the
/// real per-level native stack cost that crashed CI does not shrink just because the caller asked
/// for a larger counted-depth budget, so unlike <see cref="JsonSerializerOptions.MaxDepth"/> itself,
/// this internal safety ceiling is a hardcoded cap that a caller cannot opt out of by raising
/// <see cref="JsonSerializerOptions.MaxDepth"/>.
/// </para>
/// <para>
/// <see cref="Write{TUnion}"/> serializes the chosen arm — <c>value</c>'s own runtime type, e.g.
/// <c>Optional&lt;string&gt;.Some</c> — via
/// <c>JsonSerializer.SerializeToNode(value, armType, options)</c>, and <see cref="Read{TUnion}"/>
/// deserializes it back via <c>armNode.Deserialize(armType, options)</c>; both calls resolve
/// <c>armType</c>'s own converter the same way any other <see cref="JsonSerializer"/> call would.
/// Each factory's <c>CanConvert</c> also matches its union's arm types directly (not only the
/// closed union type itself), so that a caller whose variable is statically typed as the arm —
/// not the declared-abstract union — still resolves to this converter once the factory is
/// registered, e.g. via <see cref="DamlJsonConverters.AddDamlConverters"/>:
/// <see cref="JsonConverterAttribute"/> is not inherited by a derived type, so an attribute on the
/// union alone leaves the arm type on the default reflection-based contract, producing JSON with
/// no <c>"$case"</c> that cannot be read back as the union. But resolving <c>armType</c> through
/// the very <c>options</c> that made it match in the first place would recurse into this same
/// converter forever — on write, the "write the arm's own plain fields" step would immediately
/// rediscover "this type is a union arm" and try to write them again; on read, the arm's payload
/// has already had its <c>"$case"</c> member stripped, so the same converter re-entering on it
/// would immediately fail as if the discriminator were missing. <see cref="Write{TUnion}"/> and
/// <see cref="Read{TUnion}"/> therefore run that step against <see cref="ArmOptions"/>, a copy of
/// <c>options</c> with every <see cref="IDiscriminatedUnionJsonConverterFactory"/> removed from
/// <see cref="JsonSerializerOptions.Converters"/> — cached per <c>options</c> instance, since
/// <see cref="JsonSerializerOptions.Converters"/> takes precedence over a
/// <see cref="JsonConverterAttribute"/> on the type (see
/// <see cref="DamlJsonConverters.AddDamlConverters"/>'s remarks), removing it there is enough to
/// let <c>armType</c> fall through to its plain shape without an attribute on the arm type itself,
/// which would suffer the identical recursion with no equivalent escape. A union nested inside the
/// arm through the base type — <c>Optional&lt;Optional&lt;int&gt;&gt;</c> — is unaffected: the
/// inner <c>Optional&lt;int&gt;</c> is never itself a union's arm, so it keeps resolving through
/// its own <see cref="JsonConverterAttribute"/> exactly as before.
/// </para>
/// </remarks>
internal static class DiscriminatedUnionJson
{
    internal const string CaseProperty = "$case";
    private const int DefaultMaxDepth = 64;

    /// <summary>
    /// Fixed, platform-independent ceiling on <see cref="Write{TUnion}"/>'s own effective
    /// nesting-depth counter — see <see cref="Write{TUnion}"/>'s remarks for why a caller-configured
    /// <see cref="JsonSerializerOptions.MaxDepth"/> above this value no longer raises the depth this
    /// write path will actually recurse to.
    /// </summary>
    private const int MaxSafeWriteDepth = 20;

    [ThreadStatic]
    private static int t_depthBaseline;

    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> ArmOptionsCache = new();

    internal static TUnion Read<TUnion>(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options,
        IReadOnlyDictionary<string, Type> cases,
        string typeName)
        where TUnion : notnull
    {
        EnsureNoReferenceHandler(options, typeName);

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Expected object token for {typeName}, got {root.ValueKind}.");
        }

        if (!root.TryGetProperty(CaseProperty, out var caseProperty) || caseProperty.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"{typeName} is missing the \"{CaseProperty}\" discriminator.");
        }

        var caseName = caseProperty.GetString()!;
        if (!cases.TryGetValue(caseName, out var armType))
        {
            throw new JsonException(
                $"{typeName} names an unknown case \"{caseName}\"; expected one of {string.Join(", ", cases.Keys)}.");
        }

        try
        {
            var armNode = JsonObject.Create(root)!;
            armNode.Remove(CaseProperty);
            return (TUnion)(armNode.Deserialize(armType, ArmOptions(options))
                ?? throw new JsonException($"{typeName} case \"{caseName}\" deserialized to null."));
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot read {typeName} case \"{caseName}\": {ex.Message}", ex);
        }
    }

    internal static void Write<TUnion>(Utf8JsonWriter writer, TUnion value, JsonSerializerOptions options, string typeName)
        where TUnion : notnull
    {
        EnsureNoReferenceHandler(options, typeName);

        var armType = value.GetType();

        var configuredMaxDepth = EffectiveMaxDepth(options);
        var maxDepth = Math.Min(configuredMaxDepth, MaxSafeWriteDepth);
        var effectiveDepth = t_depthBaseline + writer.CurrentDepth;
        var stackLow = !RuntimeHelpers.TryEnsureSufficientExecutionStack();
        if (effectiveDepth >= maxDepth || stackLow)
        {
            var trigger = effectiveDepth < maxDepth
                ? $"failed because the native call stack is running low at union nesting depth {effectiveDepth} (enforced limit: {maxDepth})"
                : $"exceeded the maximum union nesting depth of {maxDepth}";
            throw new JsonException(
                $"{typeName} {trigger} while writing case "
                + $"\"{armType.Name}\": each nested union re-enters JsonSerializer.SerializeToNode as a "
                + "fresh operation, so System.Text.Json's own MaxDepth guard only runs once the whole "
                + "node tree is already built in memory and cannot be relied on to catch this before "
                + "the call stack is exhausted. The actual native call-stack cost of each counted level "
                + "varies by platform and JIT, so this also refuses once the remaining call stack itself "
                + "is running low, even short of the counted depth above, rather than let a "
                + "platform-specific stack budget be the thing that decides whether this throws a "
                + "JsonException or crashes the process. The depth enforced here is "
                + $"{maxDepth} — the smaller of the caller's configured (or default) "
                + $"JsonSerializerOptions.MaxDepth ({configuredMaxDepth}) and a fixed internal "
                + "safety ceiling that a caller cannot raise past, because the real per-level native "
                + "stack cost that motivates this ceiling does not shrink just because MaxDepth was "
                + "raised.");
        }

        var previousDepthBaseline = t_depthBaseline;
        t_depthBaseline = effectiveDepth;
        JsonNode node;
        try
        {
            node = JsonSerializer.SerializeToNode(value, armType, ArmOptions(options))
                ?? throw new JsonException($"Cannot write {typeName} case \"{armType.Name}\": serialized to null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot write {typeName} case \"{armType.Name}\": {ex.Message}", ex);
        }
        finally
        {
            t_depthBaseline = previousDepthBaseline;
        }

        if (node is not JsonObject arm)
        {
            throw new JsonException(
                $"Cannot write {typeName} case \"{armType.Name}\": expected an object, got {node.GetValueKind()}.");
        }

        arm.Insert(0, CaseProperty, JsonValue.Create(armType.Name));
        arm.WriteTo(writer);
    }

    private static void EnsureNoReferenceHandler(JsonSerializerOptions options, string typeName)
    {
        if (options.ReferenceHandler is not null)
        {
            throw new JsonException(
                $"Reference handling is not supported for {typeName}: this Daml union's converter reaches "
                + "its arm's payload through a nested JsonSerializer call that starts its own independent "
                + "reference resolver, so JsonSerializerOptions.ReferenceHandler.Preserve would silently "
                + "duplicate an aliased object under a colliding \"$id\" and ReferenceHandler.IgnoreCycles "
                + "would not recognize a cycle through this type at all. Serialize or deserialize a value "
                + $"containing {typeName} with JsonSerializerOptions.ReferenceHandler left null.");
        }
    }

    private static int EffectiveMaxDepth(JsonSerializerOptions options) =>
        options.MaxDepth == 0 ? DefaultMaxDepth : options.MaxDepth;

    private static JsonSerializerOptions ArmOptions(JsonSerializerOptions options) =>
        ArmOptionsCache.GetValue(options, static original =>
        {
            var copy = new JsonSerializerOptions(original);
            for (var i = copy.Converters.Count - 1; i >= 0; i--)
            {
                if (copy.Converters[i] is IDiscriminatedUnionJsonConverterFactory)
                {
                    copy.Converters.RemoveAt(i);
                }
            }

            return copy;
        });

    internal static string Describe(Type type)
    {
        var arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        var name = arity < 0 ? type.Name : type.Name[..arity];
        return type switch
        {
            { IsGenericType: true } =>
                $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>",
            { DeclaringType: not null } => $"{type.DeclaringType.Name}.{name}",
            _ => name,
        };
    }
}
