// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Turns a <see cref="DamlType"/> into C#: <see cref="MapType(DamlType)"/> produces a C# type
/// name, <c>ToValue</c> and <c>FromValue</c> produce the serialize and
/// deserialize expressions. Constructed once per package over a
/// <see cref="PackageEmitContext"/> and an <see cref="DarCrossPackageResolver"/>, which
/// it calls into for cross-package names — it does not own resolution. Pure functions
/// of their inputs, so unit-testable without a real DAR.
/// </summary>
internal sealed class DamlTypeMapper(PackageEmitContext context, DarCrossPackageResolver resolver)
{
    private const int MaxTypeDepth = 256;

    /// <summary>Maps <paramref name="type"/> to its C# type name.</summary>
    public string MapType(DamlType type) =>
        MapType(OptionalRepresentation.Rewrite(type, context.Package, resolver), depth: 0);

    /// <summary>
    /// The collection shape <see cref="MapType(DamlType)"/> emits for <paramref name="type"/>:
    /// <see cref="CollectionShape.List"/> for the types it writes as
    /// <c>IReadOnlyList&lt;T&gt;</c>, <see cref="CollectionShape.Map"/> for the ones it writes
    /// as <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c>, and
    /// <see cref="CollectionShape.None"/> for everything else.
    /// </summary>
    /// <param name="type">The Daml type of the member being emitted.</param>
    public CollectionShape ClassifyCollection(DamlType type) =>
        ClassifyRewritten(OptionalRepresentation.Rewrite(type, context.Package, resolver));

    private CollectionShape ClassifyRewritten(DamlType type) =>
        new ClassifyCollectionVisitor(this, depth: 0).Dispatch(type);

    private string MapType(DamlType type, int depth)
    {
        ThrowIfTooDeep(depth, nameof(MapType));

        return new MapTypeNameVisitor(this, context, resolver, depth).Dispatch(type);
    }

    private const string FallbackTypeName = "object";

    /// <summary>
    /// True for the <see cref="DamlPrimitive"/> members the catalog records as
    /// <see cref="DamlPrimitiveDisposition.SignatureOnly"/>: structural type-formers that
    /// are legal in type signatures but have no C# data-position mapping. Mirrors the
    /// signature-only arms of the three bare-primitive switches — a member added to that
    /// set must be added here too, or its applied forms fall to the unknown-application
    /// failure below with a less precise message.
    /// </summary>
    private static bool IsSignatureOnlyPrimitive(DamlPrimitive primitive) => primitive is
        DamlPrimitive.Arrow or DamlPrimitive.Update or DamlPrimitive.TypeRep
        or DamlPrimitive.Any or DamlPrimitive.AnyException or DamlPrimitive.FailureCategory;

    /// <summary>
    /// The loud failure for a signature-only builtin reaching
    /// <see cref="ClassifyCollection(DamlType)"/>: answering <see cref="CollectionShape.None"/>
    /// would let the emitter keep walking into a member it can never map, so the classification
    /// fails where the signature-only builtin is first seen, bare or applied.
    /// </summary>
    private static NotSupportedException SignatureOnlyDataPositionFailure(DamlPrimitive primitive, bool applied) =>
        new(
            $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures "
            + "but has no C# data-position mapping, so classifying the collection shape of its "
            + (applied ? "application" : "bare form")
            + " in a data position fails loudly instead of answering None and letting the emitter walk "
            + "into a member it can never map.");

    /// <summary>
    /// The loud failure for a builtin application no supported arm names: a signature-only
    /// builtin applied in a data position (named as such), or a supported type constructor
    /// applied to the wrong number of arguments. Both once fell through to the silent
    /// <see cref="FallbackTypeName"/> catch-all, which emitted knowingly-wrong C# — an
    /// <c>object</c> member backed by <c>GenericStub</c> stubs — instead of failing codegen;
    /// no cataloged builtin shape may reach that fallback anymore.
    /// </summary>
    private static NotSupportedException UnsupportedPrimitiveApplication(DamlPrimitive primitive, int argumentCount) =>
        IsSignatureOnlyPrimitive(primitive)
            ? new NotSupportedException(
                $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures "
                + $"but has no C# data-position mapping, so its application to {argumentCount} type argument(s) "
                + "in a data position fails loudly instead of silently mapping to 'object'.")
            : new NotSupportedException(
                $"Daml primitive '{primitive}' applied to {argumentCount} type argument(s) matches no supported "
                + "C# data-position mapping — the application does not carry the argument count its "
                + "type-constructor arm requires. The emitter refuses to silently map it to 'object'.");

    /// <summary>Produces the expression that serializes <paramref name="fieldName"/> of <paramref name="type"/> to a Daml value.</summary>
    /// <param name="type">The Daml type of the field being serialized.</param>
    /// <param name="fieldName">The C# expression referencing the field value.</param>
    /// <param name="typeVarDelegates">
    /// Maps a Daml type-variable name to the injected converter-delegate parameter name
    /// in scope, supplied when emitting a generic record or variant's own body so a
    /// <see cref="DamlTypeVar"/> field resolves to its converter instead of the runtime
    /// stub. <c>null</c> outside a generic body.
    /// </param>
    public string ToValue(DamlType type, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates = null) =>
        ToValue(OptionalRepresentation.Rewrite(type, context.Package, resolver), fieldName, typeVarDelegates, depth: 0);

    /// <remarks>
    /// The optional arm strips a leading <c>@</c> from the field name when deriving its local
    /// variable. Identifier sanitization escapes a field whose name is a C# keyword — <c>lock</c>,
    /// <c>class</c>, <c>event</c> — by prepending <c>@</c>, which is legal on a property but not
    /// on the local bound by the <c>is { } __name</c> pattern, so an <c>Optional</c> field called
    /// <c>lock</c> would emit the unparsable <c>__@lock</c>. Only the local is stripped; the
    /// property reference keeps its escape so the record property stays addressable.
    /// </remarks>
    private string ToValue(DamlType type, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth)
    {
        ThrowIfTooDeep(depth, nameof(ToValue));

        return new ToValueVisitor(this, resolver, fieldName, typeVarDelegates, depth).Dispatch(type);
    }

    private static string FallbackToValueStub(string fieldName) =>
        $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)}>(\"{fieldName}\")";

    private static string FallbackFromValueStub(string valueName) =>
        $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{FallbackTypeName}>(\"{valueName.Replace("\"", "\\\"", StringComparison.Ordinal)}\")";

    /// <summary>
    /// True when <see cref="MapType(DamlType)"/> renders <paramref name="type"/> as a C#
    /// reference type, so a parameter of that type can carry an
    /// <c>ArgumentNullException.ThrowIfNull</c> guard. Answers <c>false</c> for anything it
    /// cannot place with certainty, which costs a missing guard rather than a boxed
    /// value-type argument or a guard on an already-nullable parameter.
    /// </summary>
    /// <remarks>
    /// Answers over the same representation pre-pass <see cref="MapType(DamlType)"/> runs, so an
    /// Optional the pre-pass moves off C# nullable syntax and onto the wrapper is placed as the
    /// non-nullable reference type it becomes rather than as the nullable one it would have been.
    /// </remarks>
    public bool MapsToReferenceType(DamlType type) =>
        MapsToRewrittenReferenceType(OptionalRepresentation.Rewrite(type, context.Package, resolver));

    private bool MapsToRewrittenReferenceType(DamlType type) =>
        new ReferenceTypeVisitor(this, depth: 0).Dispatch(type);

    /// <summary>
    /// The JSON encoding of <paramref name="type"/> when it is an Optional, or <c>null</c> when it
    /// is not. Answers over the same representation pre-pass <see cref="MapType(DamlType)"/>
    /// runs, so a legacy <c>Optional</c> application classifies exactly like its typed node.
    /// </summary>
    internal OptionalEncoding? OptionalWireEncoding(DamlType type) =>
        new OptionalWireEncodingVisitor(this, depth: 0).Dispatch(OptionalRepresentation.Rewrite(type, context.Package, resolver));

    /// <summary>Produces the expression that deserializes <paramref name="valueName"/> back into <paramref name="type"/>.</summary>
    /// <param name="type">The Daml type to reconstruct.</param>
    /// <param name="valueName">The C# expression referencing the Daml value.</param>
    /// <param name="typeVarDelegates">
    /// Maps a Daml type-variable name to the injected converter-delegate parameter name
    /// in scope, supplied when emitting a generic record or variant's own body so a
    /// <see cref="DamlTypeVar"/> field resolves to its converter instead of the runtime
    /// stub. <c>null</c> outside a generic body.
    /// </param>
    public string FromValue(
        DamlType type,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates = null) =>
        FromValue(OptionalRepresentation.Rewrite(type, context.Package, resolver), valueName, typeVarDelegates, depth: 0);

    private string FromValue(
        DamlType type,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth)
    {
        ThrowIfTooDeep(depth, nameof(FromValue));

        return new FromValueVisitor(this, context, resolver, valueName, typeVarDelegates, depth)
            .Dispatch(type);
    }

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfJsonDecoders</c> class, spelled as a literal
    /// rather than routed through <see cref="TypeReferenceQualifier"/> — the same choice
    /// <c>ContractIdJsonConverterFactory</c> makes in <c>TemplateEmitter</c>, since this type is
    /// never an emitted member type that the shared qualifier infrastructure needs to know about.
    /// Internal, not private: <c>ChoiceEmitter</c> and <c>TemplateEmitter</c> compose their own
    /// hand-written <c>DamlLfJsonDecoders</c> calls (for a choice argument's nested record type and
    /// a template's key type respectively) alongside calls into
    /// <see cref="FromJson(DamlType, string, string, IReadOnlyDictionary{string, string})"/>,
    /// and reuse this constant rather than respelling the same literal at each of those call sites.
    /// </summary>
    internal const string DamlLfJsonDecodersQualifiedName = "global::Daml.Runtime.Serialization.DamlLfJsonDecoders";

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfElementReader</c> delegate, spelled as a
    /// literal for the same reason as <see cref="DamlLfJsonDecodersQualifiedName"/>.
    /// </summary>
    internal const string DamlLfElementReaderQualifiedName = "global::Daml.Runtime.Serialization.DamlLfElementReader";

    /// <summary>
    /// Fully qualified name of the runtime's <c>DamlLfJsonDecodeContext</c> struct, spelled as a
    /// literal for the same reason as <see cref="DamlLfJsonDecodersQualifiedName"/>.
    /// </summary>
    internal const string DamlLfJsonDecodeContextQualifiedName = "global::Daml.Runtime.Serialization.DamlLfJsonDecodeContext";

    /// <summary>
    /// Produces the expression that decodes Daml-LF JSON at <paramref name="jsonName"/> into a
    /// <c>DamlValue</c> for <paramref name="type"/>, composing calls into the runtime's
    /// <c>DamlLfJsonDecoders</c> and, for a record or variant's own type, into its emitted
    /// <c>__ReadDamlLfJson</c> capability directly — never through the constrained
    /// <c>DamlLfJsonDecoders.ReadRecord&lt;T&gt;</c>/<c>ReadVariant&lt;T&gt;</c> forwarders, which
    /// cost an extra hop generated code has no reason to pay.
    /// </summary>
    /// <param name="type">The Daml type of the value being decoded.</param>
    /// <param name="jsonName">The C# expression referencing the <c>System.Text.Json.JsonElement</c>.</param>
    /// <param name="contextName">The C# expression referencing the in-scope <c>DamlLfJsonDecodeContext</c>.</param>
    /// <param name="typeVarReaders">
    /// Maps a Daml type-variable name to the injected <c>DamlLfElementReader</c> parameter name in
    /// scope, supplied when emitting a generic record or variant's own <c>__ReadDamlLfJson</c> so a
    /// <see cref="DamlTypeVar"/> field resolves to its injected reader instead of the lazy
    /// unsupported-type leaf. <c>null</c> outside a generic body.
    /// </param>
    /// <remarks>
    /// The result is a <c>DamlValue</c>-producing expression, not a fully deserialized
    /// CLR value — a caller composes it with
    /// <see cref="FromValue(DamlType, string, IReadOnlyDictionary{string, string})"/>
    /// to reach the CLR type, exactly as the generated two-phase <c>XxxJsonReader</c> lambdas do:
    /// decode JSON to a <c>DamlValue</c> in a local, then reuse the existing, unmodified
    /// <c>FromValue</c>-generated expression on that local. A generated decoder cannot instead call
    /// its own sibling <c>XxxDecoder</c> property from within the same object initializer — object
    /// initializers bring no implicit <c>this</c> into scope, so the sibling member name would be
    /// unresolved at that point — and substituting this method's own result directly into
    /// <c>FromValue</c>'s <c>valueName</c> slot would double-evaluate the JSON decode, since the
    /// flat-<c>Optional</c> arm of <c>FromValue</c> uses <c>valueName</c> twice.
    /// </remarks>
    public string FromJson(
        DamlType type,
        string jsonName,
        string contextName,
        IReadOnlyDictionary<string, string>? typeVarReaders = null) =>
        FromJson(OptionalRepresentation.Rewrite(type, context.Package, resolver), jsonName, contextName, typeVarReaders, depth: 0);

    private string FromJson(
        DamlType type,
        string jsonName,
        string contextName,
        IReadOnlyDictionary<string, string>? typeVarReaders,
        int depth)
    {
        ThrowIfTooDeep(depth, nameof(FromJson));

        return new FromJsonVisitor(this, context, resolver, jsonName, contextName, typeVarReaders, depth)
            .Dispatch(type);
    }

    /// <summary>
    /// Builds a <c>DamlLfJsonDecoders</c> element-reader lambda decoding one composite argument of
    /// <paramref name="argument"/>'s type, for use as a <c>DamlLfElementReader</c> delegate
    /// argument to a composite reader such as <c>ReadList</c> or <c>ReadGenMap</c>.
    /// </summary>
    /// <remarks>
    /// Parameter names are suffixed by <paramref name="depth"/> rather than by argument index: two
    /// readers passed as sibling arguments to the same call (for example <c>ReadGenMap</c>'s key
    /// and value readers) can safely share the same depth, since each lambda body is an
    /// independently scoped expression and the two never see each other's parameters.
    /// </remarks>
    private string FromJsonElementReader(DamlType argument, IReadOnlyDictionary<string, string>? typeVarReaders, int depth) =>
        $"(__json{depth}, __ctx{depth}) => {FromJson(argument, $"__json{depth}", $"__ctx{depth}", typeVarReaders, depth + 1)}";

    private IReadOnlyList<string> FromJsonElementReaders(IReadOnlyList<DamlType> arguments, IReadOnlyDictionary<string, string>? typeVarReaders, int depth) =>
        arguments.Select(arg => FromJsonElementReader(arg, typeVarReaders, depth)).ToList();

    private static bool TryResolveDelegate(
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        DamlTypeVar typeVar,
        out string convert)
    {
        if (typeVarDelegates is not null && typeVarDelegates.TryGetValue(typeVar.Name, out var resolved))
        {
            convert = resolved;
            return true;
        }

        convert = string.Empty;
        return false;
    }

    /// <remarks>
    /// A switch, not a ternary, for the reason given on <see cref="MapBarePrimitiveToCSharp"/>: a
    /// ternary would emit a newly added <see cref="OptionalEncoding"/> in the wrong wire form, in
    /// code that still compiles. Only CS8524 is suppressed.
    /// </remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalSerializer(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "ToValue",
        OptionalEncoding.NestedChain => "ToChainValue",
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="WrappedOptionalSerializer"/>.</remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalDeserializer(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "FromValue",
        OptionalEncoding.NestedChain => "FromChainValue",
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="WrappedOptionalSerializer"/>.</remarks>
#pragma warning disable CS8524
    private static string WrappedOptionalJsonReader(OptionalEncoding encoding) => encoding switch
    {
        OptionalEncoding.Flat => "ReadOptional",
        OptionalEncoding.NestedChain => "ReadOptionalChain",
    };
#pragma warning restore CS8524

    /// <remarks>
    /// Only CS8524 is suppressed, never CS8509. The switch has no default arm and covers every
    /// named <see cref="DamlPrimitive"/>, so the sole uncovered input is an out-of-range cast.
    /// CS8509 — a newly added named member left unhandled — stays an error, because that warning
    /// is the compiler-enforced checklist for adding a Daml primitive.
    /// </remarks>
#pragma warning disable CS8524
    private static string MapBarePrimitiveToCSharp(DamlPrimitive primitive) => primitive switch
    {
        DamlPrimitive.Unit => TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit),
        DamlPrimitive.Bool => "bool",
        DamlPrimitive.Int64 => "long",
        DamlPrimitive.Numeric => "decimal",
        DamlPrimitive.Text => "string",
        DamlPrimitive.Date => "global::System.DateOnly",
        DamlPrimitive.Timestamp => "global::System.DateTimeOffset",
        DamlPrimitive.Party => TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party),
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the typed-node arms of MapType)."),
        DamlPrimitive.Arrow
            or DamlPrimitive.Update
            or DamlPrimitive.TypeRep
            or DamlPrimitive.Any
            or DamlPrimitive.AnyException
            or DamlPrimitive.FailureCategory =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures but has no C# data-position mapping."),
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private static string GetBarePrimitiveToValueConversion(DamlPrimitive primitive, string fieldName) => primitive switch
    {
        DamlPrimitive.Unit => $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit)}.Instance",
        DamlPrimitive.Bool => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlBool)}({fieldName})",
        DamlPrimitive.Int64 => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlInt64)}({fieldName})",
        DamlPrimitive.Numeric => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlNumeric)}({fieldName})",
        DamlPrimitive.Text => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlText)}({fieldName})",
        DamlPrimitive.Date => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlDate)}({fieldName})",
        DamlPrimitive.Timestamp => $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTimestamp)}({fieldName})",
        DamlPrimitive.Party => $"{fieldName}.ToDamlValue()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the typed-node arms of ToValue)."),
        DamlPrimitive.Arrow
            or DamlPrimitive.Update
            or DamlPrimitive.TypeRep
            or DamlPrimitive.Any
            or DamlPrimitive.AnyException
            or DamlPrimitive.FailureCategory =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures but has no C# data-position mapping."),
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private static string GetBarePrimitiveFromValueConversion(DamlPrimitive primitive, string valueName) => primitive switch
    {
        DamlPrimitive.Bool => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlBool)}>().Value",
        DamlPrimitive.Int64 => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlInt64)}>().Value",
        DamlPrimitive.Numeric => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlNumeric)}>().Value",
        DamlPrimitive.Text => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlText)}>().Value",
        DamlPrimitive.Date => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlDate)}>().Value",
        DamlPrimitive.Timestamp => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTimestamp)}>().Value",
        DamlPrimitive.Party => $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Party)}.FromDamlValue({valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlParty)}>())",
        DamlPrimitive.Unit => $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlUnit)}>()",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the typed-node arms of FromValue)."),
        DamlPrimitive.Arrow
            or DamlPrimitive.Update
            or DamlPrimitive.TypeRep
            or DamlPrimitive.Any
            or DamlPrimitive.AnyException
            or DamlPrimitive.FailureCategory =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures but has no C# data-position mapping."),
    };
#pragma warning restore CS8524

    /// <remarks>Suppresses CS8524 for the reason given on <see cref="MapBarePrimitiveToCSharp"/>.</remarks>
#pragma warning disable CS8524
    private static string GetBarePrimitiveFromJsonConversion(DamlPrimitive primitive, string jsonName, string contextName) => primitive switch
    {
        DamlPrimitive.Unit => $"{DamlLfJsonDecodersQualifiedName}.ReadUnit({jsonName}, {contextName})",
        DamlPrimitive.Bool => $"{DamlLfJsonDecodersQualifiedName}.ReadBool({jsonName}, {contextName})",
        DamlPrimitive.Int64 => $"{DamlLfJsonDecodersQualifiedName}.ReadInt64({jsonName}, {contextName})",
        DamlPrimitive.Numeric => $"{DamlLfJsonDecodersQualifiedName}.ReadNumeric({jsonName}, {contextName})",
        DamlPrimitive.Text => $"{DamlLfJsonDecodersQualifiedName}.ReadText({jsonName}, {contextName})",
        DamlPrimitive.Date => $"{DamlLfJsonDecodersQualifiedName}.ReadDate({jsonName}, {contextName})",
        DamlPrimitive.Timestamp => $"{DamlLfJsonDecodersQualifiedName}.ReadTimestamp({jsonName}, {contextName})",
        DamlPrimitive.Party => $"{DamlLfJsonDecodersQualifiedName}.ReadParty({jsonName}, {contextName})",
        DamlPrimitive.ContractId
            or DamlPrimitive.List
            or DamlPrimitive.Optional
            or DamlPrimitive.TextMap
            or DamlPrimitive.GenMap =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a type constructor and cannot appear bare — it must be applied to argument types (handled by the typed-node arms of FromJson)."),
        DamlPrimitive.Arrow
            or DamlPrimitive.Update
            or DamlPrimitive.TypeRep
            or DamlPrimitive.Any
            or DamlPrimitive.AnyException
            or DamlPrimitive.FailureCategory =>
            throw new NotSupportedException(
                $"Daml primitive '{primitive}' is a signature-only builtin — it can appear in type signatures but has no C# data-position mapping."),
    };
#pragma warning restore CS8524

    private sealed record StdlibConversion(
        Func<string, IReadOnlyList<string>, string> Serialize,
        Func<string, string, string, IReadOnlyList<string>, string> Deserialize,
        bool DeserializeTakesAbsences = false);

    private readonly StdlibConversion _recordRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToRecord({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas) =>
        {
            var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);
            return $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{damlRecordRef}>(), {string.Join(", ", lambdas)})";
        });

    private readonly StdlibConversion _tupleRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToRecord({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, convertersAndAbsences) =>
        {
            var damlRecordRef = TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord);
            return $"{stdlibName}<{typeArgs}>.FromRecord({valueName}.As<{damlRecordRef}>(), {string.Join(", ", convertersAndAbsences)})";
        },
        DeserializeTakesAbsences: true);

    private readonly StdlibConversion _valueRoundTrip = new(
        Serialize: (fieldName, lambdas) =>
            $"{fieldName}.ToValue({string.Join(", ", lambdas)})",
        Deserialize: (valueName, stdlibName, typeArgs, lambdas) =>
            $"{stdlibName}<{typeArgs}>.FromValue({valueName}, {string.Join(", ", lambdas)})");

    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion> BuildStdlibConversions() => new Dictionary<(string, string), StdlibConversion>
    {
        [("DA.Set.Types", "Set")] = _recordRoundTrip,
        [("DA.NonEmpty.Types", "NonEmpty")] = _recordRoundTrip,
        [("DA.Types", "Either")] = _valueRoundTrip,
        [("DA.Types", "Tuple2")] = _tupleRoundTrip,
        [("DA.Types", "Tuple3")] = _tupleRoundTrip,
        [("DA.Map.Types", "Map")] = _recordRoundTrip,
        [("DA.Internal.Map", "Map")] = _recordRoundTrip,
    };

    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion>? _stdlibConversions;
    private IReadOnlyDictionary<(string Module, string Name), StdlibConversion> StdlibConversions =>
        _stdlibConversions ??= BuildStdlibConversions();

    internal IReadOnlySet<(string Module, string Name)> StdlibConversionKeys =>
        StdlibConversions.Keys.ToHashSet();

    private string EmitParametricStdlibToValue(DamlTypeRef typeRef, IReadOnlyList<DamlType> arguments, string fieldName, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        ConversionFor(typeRef).Serialize(fieldName, ToValueConverterLambdas(arguments, typeVarDelegates, depth));

    private string EmitParametricStdlibFromValue(
        DamlTypeRef typeRef,
        IReadOnlyList<DamlType> arguments,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth)
    {
        var stdlibName = TypeReferenceQualifier.Qualify(
            StdlibPackages.MapStdlibType(typeRef.Module, typeRef.Name)
                ?? throw new InvalidOperationException($"No stdlib mapping for {typeRef.Module}:{typeRef.Name}"));
        var typeArgs = string.Join(", ", arguments.Select(arg => MapType(arg, depth + 1)));
        var conversion = ConversionFor(typeRef);
        return conversion.Deserialize(
            valueName,
            stdlibName,
            typeArgs,
            conversion.DeserializeTakesAbsences
                ? FromValueConvertersWithAbsences(arguments, typeVarDelegates, depth)
                : FromValueConverterLambdas(arguments, typeVarDelegates, depth));
    }

    /// <summary>
    /// Emits a <c>DamlLfJsonDecoders</c> call for a parametric stdlib type, independent of
    /// <see cref="StdlibConversions"/> — that dictionary's <see cref="StdlibConversion"/> shapes
    /// serve <see cref="ToValue(DamlType, string, IReadOnlyDictionary{string, string})"/> and
    /// <see cref="FromValue(DamlType, string, IReadOnlyDictionary{string, string})"/>'s
    /// CLR-typed conversions and do not fit the JSON reader call signatures this method builds instead.
    /// </summary>
    private string EmitParametricStdlibFromJson(
        DamlTypeRef typeRef,
        IReadOnlyList<DamlType> arguments,
        string jsonName,
        string contextName,
        IReadOnlyDictionary<string, string>? typeVarReaders,
        int depth)
    {
        var readers = FromJsonElementReaders(arguments, typeVarReaders, depth);
        return (typeRef.Module, typeRef.Name) switch
        {
            ("DA.Set.Types", "Set") => $"{DamlLfJsonDecodersQualifiedName}.ReadSet({jsonName}, {contextName}, {readers[0]})",
            ("DA.NonEmpty.Types", "NonEmpty") => $"{DamlLfJsonDecodersQualifiedName}.ReadNonEmpty({jsonName}, {contextName}, {readers[0]})",
            ("DA.Types", "Either") => $"{DamlLfJsonDecodersQualifiedName}.ReadEither({jsonName}, {contextName}, {readers[0]}, {readers[1]})",
            ("DA.Types", "Tuple2") =>
                $"{DamlLfJsonDecodersQualifiedName}.ReadTuple2({jsonName}, {contextName}, {string.Join(", ", ReadersWithAbsences(arguments, readers, typeVarReaders))})",
            ("DA.Types", "Tuple3") =>
                $"{DamlLfJsonDecodersQualifiedName}.ReadTuple3({jsonName}, {contextName}, {string.Join(", ", ReadersWithAbsences(arguments, readers, typeVarReaders))})",
            ("DA.Map.Types", "Map") or ("DA.Internal.Map", "Map") =>
                $"{DamlLfJsonDecodersQualifiedName}.ReadStdlibMap({jsonName}, {contextName}, {readers[0]}, {readers[1]})",
            _ => throw new InvalidOperationException($"No JSON stdlib conversion for {typeRef.Module}:{typeRef.Name}"),
        };
    }

    private IReadOnlyList<string> ToValueConverterLambdas(IReadOnlyList<DamlType> arguments, IReadOnlyDictionary<string, string>? typeVarDelegates, int depth) =>
        arguments.Select((arg, i) =>
            $"__t{i} => ({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)})({ToValue(arg, $"__t{i}", typeVarDelegates, depth + 1)})").ToList();

    private IReadOnlyList<string> FromValueConverterLambdas(
        IReadOnlyList<DamlType> arguments,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth) =>
        arguments.Select((arg, i) =>
            $"__v{i} => {FromValue(arg, $"__v{i}", typeVarDelegates, depth + 1)}").ToList();

    private IReadOnlyList<string> FromValueConvertersWithAbsences(
        IReadOnlyList<DamlType> arguments,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth)
    {
        var converters = FromValueConverterLambdas(arguments, typeVarDelegates, depth);
        return [.. arguments.SelectMany((argument, i) => new[] { converters[i], AbsenceArgument(argument, typeVarDelegates) })];
    }

    private IReadOnlyList<string> ReadersWithAbsences(
        IReadOnlyList<DamlType> arguments,
        IReadOnlyList<string> readers,
        IReadOnlyDictionary<string, string>? typeVarReaders) =>
        [.. arguments.SelectMany((argument, i) => new[] { readers[i], AbsenceArgument(argument, typeVarReaders) })];

    /// <summary>
    /// The expression a call site passes for what an omitted field of <paramref name="argument"/>'s
    /// type reads as: <c>DamlOptional.None</c> for a flat or a nested <c>Optional</c>, the enclosing
    /// generic body's own absence parameter for one of its type variables, and <c>null</c>, the
    /// required field, for anything else, including a type variable of an enum-like body that carries
    /// no absence parameters.
    /// </summary>
    private string AbsenceArgument(DamlType argument, IReadOnlyDictionary<string, string>? typeVarNames)
    {
        if (OptionalWireEncoding(argument) is not null)
        {
            return $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlOptional)}.None";
        }

        return argument is DamlTypeVar typeVar
            && typeVarNames is not null
            && typeVarNames.TryGetValue(EmitterHelpers.AbsenceKey(typeVar.Name), out var absenceParameter)
                ? absenceParameter
                : "null";
    }

    private StdlibConversion ConversionFor(DamlTypeRef typeRef) =>
        StdlibConversions.TryGetValue((typeRef.Module, typeRef.Name), out var conversion)
            ? conversion
            : throw new InvalidOperationException($"No stdlib conversion for {typeRef.Module}:{typeRef.Name}");

    private bool IsLocalEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalDataTypeRef(typeRef, static def => def is DamlEnumDefinition);

    private bool IsCrossPackageEnumTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlEnumDefinition);

    private bool IsEnumTypeRef(DamlTypeRef typeRef) =>
        IsLocalEnumTypeRef(typeRef) || IsCrossPackageEnumTypeRef(typeRef);

    private bool IsLocalVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalDataTypeRef(typeRef, static def => def is DamlVariantDefinition);

    private bool IsCrossPackageVariantTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlVariantDefinition);

    private bool IsVariantTypeRef(DamlTypeRef typeRef) =>
        IsLocalVariantTypeRef(typeRef) || IsCrossPackageVariantTypeRef(typeRef);

    private bool IsLocalRecordTypeRef(DamlTypeRef typeRef) =>
        IsLocalDataTypeRef(typeRef, static def => def is DamlRecordDefinition);

    private bool IsLocalDataTypeRef(DamlTypeRef typeRef, Func<DamlDataTypeDefinition, bool> matchesDefinition) =>
        context.IsLocalRef(typeRef)
        && context.DataTypes.TryGetValue($"{typeRef.Module}:{typeRef.Name}", out var dataType)
        && matchesDefinition(dataType.Definition);

    private bool IsCrossPackageRecordTypeRef(DamlTypeRef typeRef) =>
        IsCrossPackageTypeRef(typeRef, static def => def is DamlRecordDefinition);

    private bool IsRecordTypeRef(DamlTypeRef typeRef) =>
        IsLocalRecordTypeRef(typeRef) || IsCrossPackageRecordTypeRef(typeRef);

    private bool IsCrossPackageTypeRef(DamlTypeRef typeRef, Func<DamlDataTypeDefinition, bool> matchesDefinition) =>
        !context.IsLocalRef(typeRef)
        && resolver.DataTypeDefinitions(typeRef.PackageId)[(typeRef.Module, typeRef.Name)].Any(matchesDefinition);

    /// <summary>
    /// Calls an enum's <c>…Extensions</c> static method through its <c>global::</c>-rooted
    /// name: extension-method syntax binds only while the extensions class is in scope, which
    /// a rooted call never depends on.
    /// </summary>
    private string QualifiedEnumExtensionsCall(DamlTypeRef typeRef, string method, string argument) =>
        $"{resolver.Resolve(typeRef, context)}Extensions.{method}({argument})";

    private static void ThrowIfTooDeep(int depth, string operation)
    {
        if (depth > MaxTypeDepth)
        {
            throw new InvalidDataException(
                $"{operation} exceeded the maximum Daml type depth of {MaxTypeDepth}. The type is too deeply nested to emit safely.");
        }
    }

    /// <summary>
    /// The catalog arity table for the five folding builtins — the same five-node set
    /// <see cref="AppliedTypeFolding"/> folds at the reader boundaries, keyed on the catalog so
    /// a builtin moved between node and application spellings here follows the catalog, not a
    /// private copy of the table.
    /// </summary>
    private static readonly FrozenDictionary<DamlPrimitive, int> FoldingArities =
        DamlPrimitiveCatalog.Rows
            .Where(row => row.Primitive is DamlPrimitive.List or DamlPrimitive.Optional
                or DamlPrimitive.TextMap or DamlPrimitive.GenMap or DamlPrimitive.ContractId)
            .ToFrozenDictionary(row => row.Primitive!.Value, row => row.Arity);

    /// <summary>
    /// The typed node a complete application of one of the five folding builtins spells, or
    /// <c>null</c> when <paramref name="application"/> is not one: a builtin outside the
    /// folding five (the <c>Numeric n</c> scale pun included — its argument is a type
    /// variable, not a type), or a folding builtin applied to the wrong number of arguments.
    /// The caller fails loudly on <c>null</c>, so the double-representation edge holds: a
    /// hand-built legacy application that never passed a reader folds into its node and maps
    /// exactly like its normalized equivalent, and a malformed application never reaches a
    /// silent fallback.
    /// </summary>
    private static DamlType? FoldedApplication(DamlTypeApp application)
    {
        if (application.Base is not DamlPrimitiveType { Primitive: var primitive }
            || !FoldingArities.TryGetValue(primitive, out var arity)
            || application.Arguments.Count != arity)
        {
            return null;
        }

        return primitive switch
        {
            DamlPrimitive.List => new DamlListType(application.Arguments[0]),
            DamlPrimitive.Optional => new DamlOptionalType(application.Arguments[0]),
            DamlPrimitive.TextMap => new DamlTextMapType(application.Arguments[0]),
            DamlPrimitive.ContractId => new DamlContractIdType(application.Arguments[0]),
            DamlPrimitive.GenMap => new DamlGenMapType(application.Arguments[0], application.Arguments[1]),
            _ => throw new NotSupportedException(
                $"DamlPrimitive '{primitive}' is recorded in DamlPrimitiveCatalog as one of the five folding "
                + "builtins but has no typed node. Add the node mapping in DamlTypeMapper.FoldedApplication "
                + "alongside the model's typed nodes."),
        };
    }

    /// <summary>
    /// The loud failure for a generic application whose base the emitter cannot classify —
    /// the shape the old silent 'default!' fallback used to absorb. Only reachable for a
    /// type-constructor application that resolves to neither a stdlib conversion nor a
    /// record or variant, so generation fails here instead of emitting a stub.
    /// </summary>
    private static CodegenException UnclassifiableGenericApplication(DamlType type) => new(
        $"Cannot emit a deserialization expression for Daml type '{type}'. "
        + "The C# code generator does not support this type shape, so generation fails "
        + "here instead of emitting a silent 'default!' fallback into generated code.");

    /// <summary>
    /// The JSON-reader twin of <see cref="UnclassifiableGenericApplication"/>.
    /// </summary>
    private static CodegenException UnclassifiableGenericJsonApplication(DamlType type) => new(
        $"Cannot emit a JSON-decode expression for Daml type '{type}'. "
        + "The C# code generator does not support this type shape, so generation fails "
        + "here instead of emitting a silent 'default!' fallback into generated code.");

    /// <summary>
    /// The shared scaffolding of the per-operation <see cref="IDamlTypeVisitor{TResult}"/>
    /// implementations. <see cref="Dispatch"/> routes the emitter's own
    /// <see cref="DamlWrappedOptional"/> structurally — it is a representation node, not a
    /// member of the public algebra, so no visitor arm exists for it — and every public node
    /// through its own arm. <see cref="VisitTypeApp"/> carries the double-representation
    /// edge: an application of one of the five folding builtins folds into its typed node
    /// and re-dispatches to that node's arm, so a hand-built legacy application maps exactly
    /// like its normalized equivalent; the <c>Numeric n</c> pun routes to
    /// <see cref="VisitNumericApplication"/>; every other builtin application — a
    /// signature-only builtin in a data position, or a constructor applied to the wrong
    /// number of arguments — fails loudly by name; an application of a user type constructor
    /// routes to <see cref="VisitGenericApplication"/>; and an application whose base the
    /// emitter cannot represent routes to <see cref="VisitUnrepresentableApplication"/>.
    /// </summary>
    private abstract class TypeOperation<TResult>(DamlTypeMapper mapper, int depth) : IDamlTypeVisitor<TResult>
    {
        protected DamlTypeMapper Mapper { get; } = mapper;

        protected int Depth { get; } = depth;

        /// <summary>
        /// Dispatches <paramref name="type"/>: the emitter's own wrapper structurally,
        /// every public node through its own visitor arm.
        /// </summary>
        public TResult Dispatch(DamlType type) =>
            type is DamlWrappedOptional wrapped
                ? VisitWrappedOptional(wrapped)
                : type.Accept(this);

        /// <summary>Handles the emitter's own optional-representation node.</summary>
        protected abstract TResult VisitWrappedOptional(DamlWrappedOptional wrapped);

        public TResult VisitTypeApp(DamlTypeApp type)
        {
            if (type.Base is DamlPrimitiveType primitive)
            {
                if (primitive.Primitive == DamlPrimitive.Numeric)
                {
                    return VisitNumericApplication(type);
                }
                if (FoldedApplication(type) is { } folded)
                {
                    return folded.Accept(this);
                }
                throw DamlTypeMapper.UnsupportedPrimitiveApplication(primitive.Primitive, type.Arguments.Count);
            }
            if (type.Base is DamlTypeRef)
            {
                return VisitGenericApplication(type);
            }
            return VisitUnrepresentableApplication(type);
        }

        /// <summary>Handles the <c>Numeric n</c> scale pun, whose argument rides a type variable.</summary>
        protected abstract TResult VisitNumericApplication(DamlTypeApp application);

        /// <summary>Handles an application of a user-defined type constructor.</summary>
        protected abstract TResult VisitGenericApplication(DamlTypeApp application);

        /// <summary>
        /// Handles an application whose base the emitter cannot represent — a type variable
        /// or a nested application — which the mapper maps to its fallback type name.
        /// </summary>
        protected abstract TResult VisitUnrepresentableApplication(DamlTypeApp application);

        public abstract TResult VisitPrimitive(DamlPrimitiveType type);

        public abstract TResult VisitTypeRef(DamlTypeRef type);

        public abstract TResult VisitTypeVar(DamlTypeVar type);

        public abstract TResult VisitList(DamlListType type);

        public abstract TResult VisitOptional(DamlOptionalType type);

        public abstract TResult VisitTextMap(DamlTextMapType type);

        public abstract TResult VisitGenMap(DamlGenMapType type);

        public abstract TResult VisitContractId(DamlContractIdType type);
    }

    /// <summary>
    /// The type-name operation: each node's C# member type, preserving the pre-migration
    /// meaning of the shape arms — <see cref="DamlListType"/> → <c>IReadOnlyList&lt;T&gt;</c>,
    /// a surviving <see cref="DamlOptionalType"/> → <c>T?</c>, <see cref="DamlTextMapType"/> →
    /// <c>IReadOnlyDictionary&lt;string,T&gt;</c>, <see cref="DamlGenMapType"/> →
    /// <c>IReadOnlyDictionary&lt;K,V&gt;</c>, <see cref="DamlContractIdType"/> →
    /// <c>ContractId&lt;T&gt;</c>.
    /// </summary>
    private sealed class MapTypeNameVisitor(
        DamlTypeMapper mapper,
        PackageEmitContext context,
        DarCrossPackageResolver resolver,
        int depth) : TypeOperation<string>(mapper, depth)
    {
        protected override string VisitWrappedOptional(DamlWrappedOptional wrapped) =>
            $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Optional)}<{Mapper.MapType(wrapped.Argument, Depth + 1)}>";

        public override string VisitPrimitive(DamlPrimitiveType type) =>
            MapBarePrimitiveToCSharp(type.Primitive);

        public override string VisitTypeRef(DamlTypeRef type) => resolver.Resolve(type, context);

        public override string VisitTypeVar(DamlTypeVar type) => EmitterHelpers.TypeParameterName(type.Name);

        public override string VisitList(DamlListType type) =>
            $"{TypeReferenceQualifier.Qualify("IReadOnlyList")}<{Mapper.MapType(type.Element, Depth + 1)}>";

        public override string VisitOptional(DamlOptionalType type) => $"{Mapper.MapType(type.Value, Depth + 1)}?";

        public override string VisitTextMap(DamlTextMapType type) =>
            $"{TypeReferenceQualifier.Qualify("IReadOnlyDictionary")}<string, {Mapper.MapType(type.Value, Depth + 1)}>";

        public override string VisitGenMap(DamlGenMapType type) =>
            $"{TypeReferenceQualifier.Qualify("IReadOnlyDictionary")}<{Mapper.MapType(type.Key, Depth + 1)}, {Mapper.MapType(type.Value, Depth + 1)}>";

        public override string VisitContractId(DamlContractIdType type) =>
            $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{Mapper.MapType(type.Payload, Depth + 1)}>";

        protected override string VisitNumericApplication(DamlTypeApp application) => "decimal";

        protected override string VisitGenericApplication(DamlTypeApp application) =>
            application.Arguments.Count > 0
                ? $"{resolver.Resolve((DamlTypeRef)application.Base, context)}<{string.Join(", ", application.Arguments.Select(argument => Mapper.MapType(argument, Depth + 1)))}>"
                : resolver.Resolve((DamlTypeRef)application.Base, context);

        protected override string VisitUnrepresentableApplication(DamlTypeApp application) => FallbackTypeName;
    }

    /// <summary>
    /// The collection-shape operation: <see cref="CollectionShape.List"/> for the nodes the
    /// mapper writes as <c>IReadOnlyList&lt;T&gt;</c>, <see cref="CollectionShape.Map"/> for
    /// the dictionaries, <see cref="CollectionShape.None"/> for everything else — with the
    /// same loud failure the shape arms carried for a signature-only builtin in a data
    /// position, bare or applied.
    /// </summary>
    private sealed class ClassifyCollectionVisitor(DamlTypeMapper mapper, int depth)
        : TypeOperation<CollectionShape>(mapper, depth)
    {
        protected override CollectionShape VisitWrappedOptional(DamlWrappedOptional wrapped) =>
            CollectionShape.None;

        public override CollectionShape VisitPrimitive(DamlPrimitiveType type) =>
            DamlTypeMapper.IsSignatureOnlyPrimitive(type.Primitive)
                ? throw DamlTypeMapper.SignatureOnlyDataPositionFailure(type.Primitive, applied: false)
                : CollectionShape.None;

        public override CollectionShape VisitTypeRef(DamlTypeRef type) => CollectionShape.None;

        public override CollectionShape VisitTypeVar(DamlTypeVar type) => CollectionShape.None;

        public override CollectionShape VisitList(DamlListType type) => CollectionShape.List;

        public override CollectionShape VisitTextMap(DamlTextMapType type) => CollectionShape.Map;

        public override CollectionShape VisitGenMap(DamlGenMapType type) => CollectionShape.Map;

        public override CollectionShape VisitContractId(DamlContractIdType type) => CollectionShape.None;

        public override CollectionShape VisitOptional(DamlOptionalType type) => Dispatch(type.Value);

        protected override CollectionShape VisitNumericApplication(DamlTypeApp application) =>
            CollectionShape.None;

        protected override CollectionShape VisitGenericApplication(DamlTypeApp application) =>
            CollectionShape.None;

        protected override CollectionShape VisitUnrepresentableApplication(DamlTypeApp application) =>
            CollectionShape.None;
    }

    /// <summary>
    /// The reference-type operation: answers whether the mapper renders the node as a C#
    /// reference type, so a parameter of that type can carry a null guard. Answers
    /// <c>false</c> for anything it cannot place with certainty, which costs a missing guard
    /// rather than a boxed value-type argument or a guard on an already-nullable parameter.
    /// </summary>
    private sealed class ReferenceTypeVisitor(DamlTypeMapper mapper, int depth)
        : TypeOperation<bool>(mapper, depth)
    {
        protected override bool VisitWrappedOptional(DamlWrappedOptional wrapped) => true;

        public override bool VisitPrimitive(DamlPrimitiveType type) =>
            type.Primitive == DamlPrimitive.Text;

        public override bool VisitTypeRef(DamlTypeRef type) => !Mapper.IsEnumTypeRef(type);

        public override bool VisitTypeVar(DamlTypeVar type) => false;

        public override bool VisitList(DamlListType type) => true;

        public override bool VisitOptional(DamlOptionalType type) => false;

        public override bool VisitTextMap(DamlTextMapType type) => true;

        public override bool VisitGenMap(DamlGenMapType type) => true;

        public override bool VisitContractId(DamlContractIdType type) => true;

        protected override bool VisitNumericApplication(DamlTypeApp application) => false;

        protected override bool VisitGenericApplication(DamlTypeApp application) =>
            !Mapper.IsEnumTypeRef((DamlTypeRef)application.Base);

        protected override bool VisitUnrepresentableApplication(DamlTypeApp application) => false;
    }

    /// <summary>
    /// The Optional-encoding operation: the wrapper's own encoding, <see cref="OptionalEncoding.Flat"/>
    /// for a surviving <see cref="DamlOptionalType"/>, and <c>null</c> for every non-Optional node.
    /// </summary>
    private sealed class OptionalWireEncodingVisitor(DamlTypeMapper mapper, int depth)
        : TypeOperation<OptionalEncoding?>(mapper, depth)
    {
        protected override OptionalEncoding? VisitWrappedOptional(DamlWrappedOptional wrapped) => wrapped.Encoding;

        public override OptionalEncoding? VisitOptional(DamlOptionalType type) => OptionalEncoding.Flat;

        public override OptionalEncoding? VisitPrimitive(DamlPrimitiveType type) => null;

        public override OptionalEncoding? VisitTypeRef(DamlTypeRef type) => null;

        public override OptionalEncoding? VisitTypeVar(DamlTypeVar type) => null;

        public override OptionalEncoding? VisitList(DamlListType type) => null;

        public override OptionalEncoding? VisitTextMap(DamlTextMapType type) => null;

        public override OptionalEncoding? VisitGenMap(DamlGenMapType type) => null;

        public override OptionalEncoding? VisitContractId(DamlContractIdType type) => null;

        protected override OptionalEncoding? VisitNumericApplication(DamlTypeApp application) => null;

        protected override OptionalEncoding? VisitGenericApplication(DamlTypeApp application) => null;

        protected override OptionalEncoding? VisitUnrepresentableApplication(DamlTypeApp application) => null;
    }

    /// <summary>
    /// The serialize operation: the expression that turns a field of the node's type into a
    /// <c>DamlValue</c>, preserving the pre-migration meaning of the shape arms — including
    /// the optional arm's <c>@</c>-stripped local and the wrapper's converter-lambda
    /// composition per encoding.
    /// </summary>
    private sealed class ToValueVisitor(
        DamlTypeMapper mapper,
        DarCrossPackageResolver resolver,
        string fieldName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth) : TypeOperation<string>(mapper, depth)
    {
        protected override string VisitWrappedOptional(DamlWrappedOptional wrapped) =>
            $"{fieldName}.{DamlTypeMapper.WrappedOptionalSerializer(wrapped.Encoding)}(__optional{Depth} => "
            + $"{Mapper.ToValue(wrapped.Argument, $"__optional{Depth}", typeVarDelegates, Depth + 1)})";

        public override string VisitPrimitive(DamlPrimitiveType type) =>
            GetBarePrimitiveToValueConversion(type.Primitive, fieldName);

        public override string VisitTypeRef(DamlTypeRef type)
        {
            if (Mapper.IsEnumTypeRef(type))
            {
                return Mapper.QualifiedEnumExtensionsCall(type, "ToDamlEnum", fieldName);
            }
            if (Mapper.IsVariantTypeRef(type))
            {
                return $"{fieldName}.ToVariant()";
            }
            return $"{fieldName}.ToRecord()";
        }

        public override string VisitTypeVar(DamlTypeVar type) =>
            DamlTypeMapper.TryResolveDelegate(typeVarDelegates, type, out var convert)
                ? $"{convert}({fieldName})"
                : FallbackToValueStub(fieldName);

        public override string VisitList(DamlListType type) =>
            $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlList)}({fieldName}.Select(x => "
            + $"({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)})"
            + $"{Mapper.ToValue(type.Element, "x", typeVarDelegates, Depth + 1)}).ToList())";

        public override string VisitOptional(DamlOptionalType type) =>
            $"{fieldName} is {{ }} __{fieldName.TrimStart('@')} ? "
            + $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlOptional)}"
            + $"({Mapper.ToValue(type.Value, $"__{fieldName.TrimStart('@')}", typeVarDelegates, Depth + 1)}) "
            + $": {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlOptional)}.None";

        public override string VisitTextMap(DamlTextMapType type) =>
            $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTextMap)}({fieldName}.ToDictionary(kv => kv.Key, kv => "
            + $"({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)})"
            + $"{Mapper.ToValue(type.Value, "kv.Value", typeVarDelegates, Depth + 1)}))";

        public override string VisitGenMap(DamlGenMapType type) =>
            $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlGenMap)}({fieldName}.Select(kv => ("
            + $"({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)})"
            + $"{Mapper.ToValue(type.Key, "kv.Key", typeVarDelegates, Depth + 1)}, "
            + $"({TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlValue)})"
            + $"{Mapper.ToValue(type.Value, "kv.Value", typeVarDelegates, Depth + 1)})).ToList())";

        public override string VisitContractId(DamlContractIdType type) => $"{fieldName}.ToDamlValue()";

        protected override string VisitNumericApplication(DamlTypeApp application) =>
            $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlNumeric)}({fieldName})";

        protected override string VisitGenericApplication(DamlTypeApp application)
        {
            var typeRef = (DamlTypeRef)application.Base;
            if (StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true))
            {
                return Mapper.EmitParametricStdlibToValue(
                    typeRef, application.Arguments, fieldName, typeVarDelegates, Depth);
            }
            if (Mapper.IsVariantTypeRef(typeRef))
            {
                return $"{fieldName}.ToVariant({string.Join(", ", Mapper.ToValueConverterLambdas(application.Arguments, typeVarDelegates, Depth))})";
            }
            if (Mapper.IsRecordTypeRef(typeRef))
            {
                return $"{fieldName}.ToRecord({string.Join(", ", Mapper.ToValueConverterLambdas(application.Arguments, typeVarDelegates, Depth))})";
            }
            return $"{fieldName}.ToRecord()";
        }

        protected override string VisitUnrepresentableApplication(DamlTypeApp application) =>
            FallbackToValueStub(fieldName);
    }

    /// <summary>
    /// The deserialize operation: the expression that turns a <c>DamlValue</c> back into the
    /// node's C# type, preserving the pre-migration meaning of the shape arms — including the
    /// declared-collection casts, the root-qualified <c>DamlRecord</c> reference the nested
    /// choice-argument names can shadow, and the wrapper's per-encoding converter pair.
    /// </summary>
    private sealed class FromValueVisitor(
        DamlTypeMapper mapper,
        PackageEmitContext context,
        DarCrossPackageResolver resolver,
        string valueName,
        IReadOnlyDictionary<string, string>? typeVarDelegates,
        int depth) : TypeOperation<string>(mapper, depth)
    {
        protected override string VisitWrappedOptional(DamlWrappedOptional wrapped) =>
            $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.Optional)}<{Mapper.MapType(wrapped.Argument, Depth + 1)}>"
            + $".{DamlTypeMapper.WrappedOptionalDeserializer(wrapped.Encoding)}({valueName}, __optional{Depth} => "
            + $"{Mapper.FromValue(wrapped.Argument, $"__optional{Depth}", typeVarDelegates, Depth + 1)})";

        public override string VisitPrimitive(DamlPrimitiveType type) =>
            GetBarePrimitiveFromValueConversion(type.Primitive, valueName);

        public override string VisitTypeRef(DamlTypeRef type)
        {
            if (Mapper.IsEnumTypeRef(type))
            {
                return Mapper.QualifiedEnumExtensionsCall(
                    type, "FromDamlEnum", $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlEnum)}>()");
            }
            if (Mapper.IsVariantTypeRef(type))
            {
                return $"{resolver.Resolve(type, context)}.FromVariant({valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlVariant)}>())";
            }
            return $"{resolver.Resolve(type, context)}.FromRecord({valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord)}>())";
        }

        public override string VisitTypeVar(DamlTypeVar type) =>
            DamlTypeMapper.TryResolveDelegate(typeVarDelegates, type, out var convert)
                ? $"{convert}({valueName})"
                : $"{TypeReferenceQualifier.Qualify(RuntimeTypeNames.GenericStub)}.NotImplemented<{EmitterHelpers.TypeParameterName(type.Name)}>(\"{type.Name}\")";

        public override string VisitList(DamlListType type) =>
            $"({TypeReferenceQualifier.Qualify("IReadOnlyList")}<{Mapper.MapType(type.Element, Depth + 1)}>)"
            + $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlList)}>().Values"
            + $".Select(x => {Mapper.FromValue(type.Element, "x", typeVarDelegates, Depth + 1)}).ToList()";

        public override string VisitOptional(DamlOptionalType type) =>
            $"{valueName}.AsOptional().HasValue ? "
            + $"{Mapper.FromValue(type.Value, $"{valueName}.AsOptional().Value!", typeVarDelegates, Depth + 1)} "
            + ": null";

        public override string VisitTextMap(DamlTextMapType type) =>
            $"({TypeReferenceQualifier.Qualify("IReadOnlyDictionary")}<string, {Mapper.MapType(type.Value, Depth + 1)}>)"
            + $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlTextMap)}>().Values"
            + $".ToDictionary(kv => kv.Key, kv => "
            + $"{Mapper.FromValue(type.Value, "kv.Value", typeVarDelegates, Depth + 1)})";

        public override string VisitGenMap(DamlGenMapType type) =>
            $"({TypeReferenceQualifier.Qualify("IReadOnlyDictionary")}<{Mapper.MapType(type.Key, Depth + 1)}, {Mapper.MapType(type.Value, Depth + 1)}>)"
            + $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlGenMap)}>().Entries"
            + $".ToDictionary(kv => {Mapper.FromValue(type.Key, "kv.Key", typeVarDelegates, Depth + 1)}, "
            + $"kv => {Mapper.FromValue(type.Value, "kv.Value", typeVarDelegates, Depth + 1)})";

        public override string VisitContractId(DamlContractIdType type) =>
            $"new {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ContractId)}<{Mapper.MapType(type.Payload, Depth + 1)}>"
            + $"({valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlContractId)}>().Value)";

        protected override string VisitNumericApplication(DamlTypeApp application) =>
            $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlNumeric)}>().Value";

        protected override string VisitGenericApplication(DamlTypeApp application)
        {
            var typeRef = (DamlTypeRef)application.Base;
            if (StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true))
            {
                return Mapper.EmitParametricStdlibFromValue(
                    typeRef, application.Arguments, valueName, typeVarDelegates, Depth);
            }
            var typeArguments =
                $"<{string.Join(", ", application.Arguments.Select(argument => Mapper.MapType(argument, Depth + 1)))}>";
            if (Mapper.IsVariantTypeRef(typeRef))
            {
                return $"{resolver.Resolve(typeRef, context)}{typeArguments}.FromVariant("
                    + $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlVariant)}>(), "
                    + $"{string.Join(", ", Mapper.FromValueConvertersWithAbsences(application.Arguments, typeVarDelegates, Depth))})";
            }
            if (Mapper.IsRecordTypeRef(typeRef))
            {
                return $"{resolver.Resolve(typeRef, context)}{typeArguments}.FromRecord("
                    + $"{valueName}.As<{TypeReferenceQualifier.Qualify(RuntimeTypeNames.DamlRecord)}>(), "
                    + $"{string.Join(", ", Mapper.FromValueConvertersWithAbsences(application.Arguments, typeVarDelegates, Depth))})";
            }
            throw DamlTypeMapper.UnclassifiableGenericApplication(application);
        }

        protected override string VisitUnrepresentableApplication(DamlTypeApp application) =>
            FallbackFromValueStub(valueName);
    }

    /// <summary>
    /// The JSON-decode operation: the <c>DamlLfJsonDecoders</c> call decoding a
    /// <c>JsonElement</c> into a <c>DamlValue</c> for the node's type, composing the element
    /// readers the composite readers take as delegates, and routing a record, variant or enum
    /// reference straight into that type's emitted <c>__ReadDamlLfJson</c> — a generic
    /// application passes one element reader per type argument.
    /// </summary>
    private sealed class FromJsonVisitor(
        DamlTypeMapper mapper,
        PackageEmitContext context,
        DarCrossPackageResolver resolver,
        string jsonName,
        string contextName,
        IReadOnlyDictionary<string, string>? typeVarReaders,
        int depth) : TypeOperation<string>(mapper, depth)
    {
        protected override string VisitWrappedOptional(DamlWrappedOptional wrapped) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.{DamlTypeMapper.WrappedOptionalJsonReader(wrapped.Encoding)}"
            + $"({jsonName}, {contextName}, {ElementReader(wrapped.Argument)})";

        public override string VisitPrimitive(DamlPrimitiveType type) =>
            DamlTypeMapper.GetBarePrimitiveFromJsonConversion(type.Primitive, jsonName, contextName);

        public override string VisitTypeRef(DamlTypeRef type) =>
            Mapper.IsEnumTypeRef(type)
                ? Mapper.QualifiedEnumExtensionsCall(type, "__ReadDamlLfJson", $"{jsonName}, {contextName}")
                : $"{resolver.Resolve(type, context)}.__ReadDamlLfJson({jsonName}, {contextName})";

        public override string VisitTypeVar(DamlTypeVar type) =>
            DamlTypeMapper.TryResolveDelegate(typeVarReaders, type, out var read)
                ? $"{read}({jsonName}, {contextName})"
                : ReadUnsupported(type.Name);

        public override string VisitList(DamlListType type) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadList({jsonName}, {contextName}, {ElementReader(type.Element)})";

        public override string VisitOptional(DamlOptionalType type) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadOptional({jsonName}, {contextName}, {ElementReader(type.Value)})";

        public override string VisitTextMap(DamlTextMapType type) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadTextMap({jsonName}, {contextName}, {ElementReader(type.Value)})";

        public override string VisitGenMap(DamlGenMapType type) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadGenMap({jsonName}, {contextName}, "
            + $"{ElementReader(type.Key)}, {ElementReader(type.Value)})";

        public override string VisitContractId(DamlContractIdType type) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadContractId({jsonName}, {contextName})";

        protected override string VisitNumericApplication(DamlTypeApp application) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadNumeric({jsonName}, {contextName})";

        protected override string VisitGenericApplication(DamlTypeApp application)
        {
            var typeRef = (DamlTypeRef)application.Base;
            if (StdlibPackages.IsStdlibTypeRef(resolver, typeRef, parametric: true))
            {
                return Mapper.EmitParametricStdlibFromJson(
                    typeRef, application.Arguments, jsonName, contextName, typeVarReaders, Depth);
            }
            if (!Mapper.IsVariantTypeRef(typeRef) && !Mapper.IsRecordTypeRef(typeRef))
            {
                throw DamlTypeMapper.UnclassifiableGenericJsonApplication(application);
            }
            if (application.Arguments.Count == 0)
            {
                return $"{resolver.Resolve(typeRef, context)}.__ReadDamlLfJson({jsonName}, {contextName})";
            }
            var typeArguments =
                $"<{string.Join(", ", application.Arguments.Select(argument => Mapper.MapType(argument, Depth + 1)))}>";
            var elementReaders = Mapper.FromJsonElementReaders(application.Arguments, typeVarReaders, Depth);
            var readerArguments = Mapper.ReadersWithAbsences(application.Arguments, elementReaders, typeVarReaders);
            return $"{resolver.Resolve(typeRef, context)}{typeArguments}.__ReadDamlLfJson("
                + $"{jsonName}, {contextName}, {string.Join(", ", readerArguments)})";
        }

        protected override string VisitUnrepresentableApplication(DamlTypeApp application) =>
            ReadUnsupported(application.ToString());

        private string ElementReader(DamlType argument) =>
            Mapper.FromJsonElementReader(argument, typeVarReaders, Depth);

        private string ReadUnsupported(string damlTypeDescription) =>
            $"{DamlTypeMapper.DamlLfJsonDecodersQualifiedName}.ReadUnsupported({jsonName}, {contextName}, \"{damlTypeDescription}\")";
    }
}
