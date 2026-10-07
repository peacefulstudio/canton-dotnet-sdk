// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace Canton.Ledger.Abstractions;

internal sealed class PqsPredicateTranslator
{
    private const string VariantTagProperty = "Tag";
    private const string VariantValueProperty = "Value";
    private const string VariantTagField = "tag";
    private const string VariantValueField = "value";

    private readonly Dictionary<ParameterExpression, Cursor> scopes = [];
    private readonly string paramName;
    private int aliasCount;

    private PqsPredicateTranslator(ParameterExpression root, string paramName)
    {
        scopes[root] = Cursor.Root(PqsScope.Payload, root.Type);
        this.paramName = paramName;
    }

    public static PqsFilter FieldEquals(LambdaExpression selector, string value)
    {
        var translator = new PqsPredicateTranslator(selector.Parameters[0], nameof(selector));
        var node = UnwrapOptionals(translator.Node(StripBoxing(selector.Body)));
        var leaf = translator.LeafOf(node);
        return new PqsCompare(ComparedPath(node, leaf), PqsComparison.Equal, leaf.ParseOperand(value, nameof(value)));
    }

    public static PqsFilter Where(LambdaExpression predicate) =>
        new PqsPredicateTranslator(predicate.Parameters[0], nameof(predicate)).Predicate(predicate.Body);

    private PqsFilter Predicate(Expression expression) =>
        expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } and =>
                new PqsAll([Predicate(and.Left), Predicate(and.Right)]),
            BinaryExpression { NodeType: ExpressionType.OrElse } or =>
                new PqsAny([Predicate(or.Left), Predicate(or.Right)]),
            UnaryExpression { NodeType: ExpressionType.Not } not => new PqsNot(Predicate(not.Operand)),
            BinaryExpression binary when ComparisonOf(binary.NodeType) is not null => Comparison(binary),
            MemberExpression { Member.Name: nameof(Nullable<int>.HasValue), Expression: { } optional }
                when IsOptional(optional.Type) => OptionalPresence(Node(optional), isSome: true),
            TypeBinaryExpression { NodeType: ExpressionType.TypeIs } typeIs => TypeTest(typeIs),
            MethodCallExpression { Method.Name: nameof(Enumerable.Any) or nameof(Enumerable.All) or nameof(Enumerable.Contains) } call
                when call.Method.DeclaringType == typeof(Enumerable) && IsList(call.Arguments[0].Type) => ListPredicate(call),
            MethodCallExpression { Method.Name: nameof(IReadOnlyDictionary<int, int>.ContainsKey), Object: { } map, Arguments: [var key] }
                when MapTypes(map.Type) is not null => KeyPresence(Node(map), key),
            _ when expression.Type == typeof(bool) && ReferencesScope(expression) =>
                Compare(Node(expression), ExpressionType.Equal, true),
            _ => throw Unsupported(expression),
        };

    private PqsFilter Comparison(BinaryExpression binary)
    {
        var leftIsPayload = ReferencesScope(binary.Left);
        var rightIsPayload = ReferencesScope(binary.Right);
        if (leftIsPayload == rightIsPayload)
            throw new ArgumentException(
                $"Unsupported comparison '{binary}': one side must be a payload field and the other side must be a captured value.",
                paramName);

        var (payloadSide, comparison, value) = leftIsPayload
            ? (binary.Left, binary.NodeType, Evaluate(binary.Right))
            : (binary.Right, Flip(binary.NodeType), Evaluate(binary.Left));

        return value is null
            && comparison == ExpressionType.Equal
            && payloadSide is UnaryExpression { NodeType: ExpressionType.TypeAs, Operand: { } variant } cast
            && IsVariantConstructor(cast.Type, variant.Type)
            && Cursor.OptionalElementType(variant.Type) is null
            ? ConstructorAbsence(Node(variant), cast.Type)
            : Compare(Node(payloadSide), comparison, value);
    }

    private PqsFilter ConstructorAbsence(Cursor variant, Type constructorType) =>
        Compare(variant.Field(VariantTagField, typeof(string)), ExpressionType.NotEqual, TagOf(constructorType));

    private PqsFilter Compare(Cursor node, ExpressionType comparison, object? value)
    {
        if (value is null)
            return CompareWithNull(node, comparison);
        var leaf = LeafOf(node);
        if (!leaf.IsOrdered && comparison is not (ExpressionType.Equal or ExpressionType.NotEqual))
            throw new ArgumentException(
                $"A Daml {leaf.DamlTypeName} field only supports == and != in a PQS filter.", paramName);
        var path = ComparedPath(node, leaf);
        return new PqsCompare(path, ComparisonOf(comparison)!.Value, leaf.Operand(value));
    }

    private static PqsPath ComparedPath(Cursor node, PqsLeafType leaf) =>
        node.DefaultsWhenAbsent ? new PqsDefaulted(node.Path, leaf.DefaultOperand(node.ClrType)) : node.Path;

    private static Cursor UnwrapOptionals(Cursor node)
    {
        while (Cursor.OptionalElementType(node.ClrType) is { } element)
            node = node.OptionalValue(element);
        return node;
    }

    private Cursor OptionalRead(Cursor optional, MethodCallExpression call)
    {
        var value = optional.OptionalValue(call.Type);
        if (call.Method.Name != nameof(Nullable<int>.GetValueOrDefault) || !call.Type.IsValueType)
            return value;
        if (PqsLeafType.For(call.Type) is { HasDefault: false })
            throw new ArgumentException(
                $"GetValueOrDefault() on an Optional<{call.Type.Name}> is not supported in a PQS filter: " +
                $"the C# default of {call.Type.Name} has no SQL form. Test .HasValue or use .Value instead.",
                paramName);
        return value.DefaultingWhenAbsent();
    }

    private PqsFilter CompareWithNull(Cursor node, ExpressionType comparison)
    {
        if (comparison is not (ExpressionType.Equal or ExpressionType.NotEqual))
            throw new ArgumentException("A null value can be compared only with == and != in a PQS filter.", paramName);
        if (Cursor.OptionalElementType(node.ClrType) is not null)
            throw new ArgumentException(
                $"An Optional<T> field is never null; test it with .HasValue or an 'is Optional<T>.None' pattern instead.",
                paramName);
        return OptionalPresence(node, isSome: comparison == ExpressionType.NotEqual);
    }

    private static PqsFilter OptionalPresence(Cursor node, bool isSome) =>
        new PqsOptionalPresence(node.Path, isSome, node.IsListEncodedOptional);

    private PqsFilter TypeTest(TypeBinaryExpression typeIs)
    {
        var node = Node(typeIs.Expression);
        if (Cursor.OptionalElementType(typeIs.Expression.Type) is { } element)
        {
            if (typeIs.TypeOperand == typeof(Optional<>.Some).MakeGenericType(element))
                return OptionalPresence(node, isSome: true);
            if (typeIs.TypeOperand == typeof(Optional<>.None).MakeGenericType(element))
                return OptionalPresence(node, isSome: false);
        }

        if (IsVariantConstructor(typeIs.TypeOperand, typeIs.Expression.Type))
            return Compare(node.Field(VariantTagField, typeof(string)), ExpressionType.Equal, TagOf(typeIs.TypeOperand));

        throw Unsupported(typeIs);
    }

    private PqsFilter ListPredicate(MethodCallExpression call)
    {
        var list = Node(call.Arguments[0]).Path;
        var scope = PqsScope.Element(aliasCount++);
        var element = Cursor.Root(scope, call.Method.GetGenericArguments()[0]);

        return call switch
        {
            { Method.Name: nameof(Enumerable.Any), Arguments.Count: 1 } => new PqsListAny(list, scope, null),
            { Method.Name: nameof(Enumerable.Any), Arguments: [_, LambdaExpression condition] } =>
                new PqsListAny(list, scope, ElementPredicate(condition, element)),
            { Method.Name: nameof(Enumerable.All), Arguments: [_, LambdaExpression condition] } =>
                new PqsListAll(list, scope, ElementPredicate(condition, element)),
            { Method.Name: nameof(Enumerable.Contains), Arguments: [_, var item] } when !ReferencesScope(item) =>
                new PqsListAny(list, scope, Compare(element, ExpressionType.Equal, Evaluate(item))),
            _ => throw Unsupported(call),
        };
    }

    private PqsFilter ElementPredicate(LambdaExpression condition, Cursor element)
    {
        scopes[condition.Parameters[0]] = element;
        return Predicate(condition.Body);
    }

    private PqsFilter KeyPresence(Cursor map, Expression key) => new PqsNotNull(MapLookup(map, key).Path);

    private Cursor MapLookup(Cursor map, Expression keyExpression)
    {
        if (ReferencesScope(keyExpression))
            throw new ArgumentException(
                $"Unsupported Map lookup '{keyExpression}': the Map key must be a captured value, not a payload field.",
                paramName);
        var (keyType, valueType) = MapTypes(map.ClrType)!.Value;
        var key = Evaluate(keyExpression)!;
        var keyLeaf = PqsLeafType.For(keyType)
            ?? throw new ArgumentException($"A '{keyType.Name}' Map key is not supported in a PQS filter.", paramName);

        return map.MapEntry(keyLeaf.Operand(key), aliasCount++, valueType);
    }

    private PqsLeafType LeafOf(Cursor node) =>
        PqsLeafType.For(node.ClrType)
        ?? throw new ArgumentException(
            $"'{node.ClrType.Name}' is not a Daml leaf type; select one of its fields instead.", paramName);

    private Cursor Node(Expression expression) =>
        expression switch
        {
            ParameterExpression parameter when scopes.TryGetValue(parameter, out var scope) => scope,
            MemberExpression { Expression: { } parent } member => Member(Node(parent), member),
            MethodCallExpression { Method.Name: "get_Item", Object: { } map, Arguments: [var key] }
                when MapTypes(map.Type) is not null => MapLookup(Node(map), key),
            MethodCallExpression { Object: { } optional, Arguments.Count: 0, Method.Name: "GetValueOrDefault" or "GetValueOrThrow" } call
                when IsOptional(optional.Type) => OptionalRead(Node(optional), call),
            UnaryExpression { NodeType: ExpressionType.Convert, Operand: { } optional } someCast
                when IsOptionalSome(someCast.Type, optional.Type) => SomeCast(Node(optional), someCast.Type),
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs, Operand: { } variant } constructorCast
                when IsVariantConstructor(constructorCast.Type, variant.Type) =>
                ConstructorCast(Node(variant), constructorCast.Type),
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
                when IsRepresentationPreserving(convert.Operand.Type, convert.Type) => Node(convert.Operand),
            _ => throw Unsupported(expression),
        };

    private Cursor Member(Cursor parent, MemberExpression member)
    {
        if (member.Member.Name == nameof(Nullable<int>.Value) && IsOptional(member.Expression!.Type))
            return parent.OptionalValue(member.Type);
        if (member.Member.Name == VariantValueProperty && IsVariantConstructor(member.Expression!.Type, member.Expression.Type.BaseType))
            return parent.Field(VariantValueField, member.Type);
        if (IsTuple(member.Member.DeclaringType))
            return parent.Field(member.Member.Name, member.Type);
        if (member.Member.GetCustomAttribute<DamlFieldAttribute>() is { } attribute)
            return parent.Field(attribute.Name, member.Type);
        if (!IsGeneratedRecordMember(member.Member))
            throw Unsupported(member);
        throw new InvalidOperationException(
            $"Property '{member.Member.DeclaringType?.Name}.{member.Member.Name}' carries no [DamlField] metadata, so its " +
            $"PQS wire field name cannot be resolved. Regenerate the Daml bindings with a codegen " +
            $"that emits field-name metadata; the typed filter DSL reads the wire name from that " +
            $"attribute and never guesses from the C# property name.");
    }

    private static bool IsGeneratedRecordMember(MemberInfo member) =>
        member.DeclaringType is { } declaringType && typeof(IDamlRecord).IsAssignableFrom(declaringType);

    private static Cursor ConstructorCast(Cursor variant, Type constructorType) =>
        variant.ConstructorCast(TagOf(constructorType), constructorType);

    private static string TagOf(Type constructorType) =>
        constructorType.GetProperty(VariantTagProperty, BindingFlags.Public | BindingFlags.Instance) is { PropertyType: var tagType } tag
        && tagType == typeof(string)
            ? (string)tag.GetValue(RuntimeHelpers.GetUninitializedObject(constructorType))!
            : constructorType.Name;

    private static bool IsVariantConstructor(Type candidate, Type? variant) =>
        variant is not null
        && candidate.IsSealed
        && candidate.BaseType == variant
        && candidate.DeclaringType == (variant.IsGenericType ? variant.GetGenericTypeDefinition() : variant);

    private static bool IsList(Type type) => ImplementedGeneric(type, typeof(IReadOnlyList<>)) is not null;

    private static (Type Key, Type Value)? MapTypes(Type type) =>
        ImplementedGeneric(type, typeof(IReadOnlyDictionary<,>)) is { } map
            ? (map.GetGenericArguments()[0], map.GetGenericArguments()[1])
            : null;

    private static Type? ImplementedGeneric(Type type, Type genericInterface) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);

    private static bool IsTuple(Type? declaringType) =>
        declaringType is { IsGenericType: true }
        && declaringType.GetGenericTypeDefinition() is var definition
        && (definition == typeof(Tuple2<,>) || definition == typeof(Tuple3<,,>));

    private static Cursor SomeCast(Cursor optional, Type someType) => optional.SomeCast(someType);

    private static bool IsOptional(Type type) =>
        Nullable.GetUnderlyingType(type) is not null || Cursor.OptionalElementType(type) is not null;

    private static bool IsOptionalSome(Type target, Type source) =>
        Cursor.OptionalElementType(source) is { } element
        && target == typeof(Optional<>.Some).MakeGenericType(element);

    private static bool IsRepresentationPreserving(Type from, Type to) =>
        to == typeof(object)
        || Nullable.GetUnderlyingType(to) == from
        || IsEnumToUnderlying(from, to)
        || (Nullable.GetUnderlyingType(from) is { } fromValue
            && Nullable.GetUnderlyingType(to) is { } toValue
            && IsEnumToUnderlying(fromValue, toValue));

    private static bool IsEnumToUnderlying(Type from, Type to) => from.IsEnum && to == Enum.GetUnderlyingType(from);

    private bool ReferencesScope(Expression expression)
    {
        var finder = new ScopeReferenceFinder(scopes);
        finder.Visit(expression);
        return finder.Found;
    }

    private static object? Evaluate(Expression expression) =>
        expression is ConstantExpression constant
            ? constant.Value
            : Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object)))
                .Compile(preferInterpretation: true)();

    private static PqsComparison? ComparisonOf(ExpressionType comparison) =>
        comparison switch
        {
            ExpressionType.Equal => PqsComparison.Equal,
            ExpressionType.NotEqual => PqsComparison.NotEqual,
            ExpressionType.LessThan => PqsComparison.LessThan,
            ExpressionType.LessThanOrEqual => PqsComparison.LessThanOrEqual,
            ExpressionType.GreaterThan => PqsComparison.GreaterThan,
            ExpressionType.GreaterThanOrEqual => PqsComparison.GreaterThanOrEqual,
            _ => null,
        };

    private static ExpressionType Flip(ExpressionType comparison) =>
        comparison switch
        {
            ExpressionType.LessThan => ExpressionType.GreaterThan,
            ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
            ExpressionType.GreaterThan => ExpressionType.LessThan,
            ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
            _ => comparison,
        };

    private static Expression StripBoxing(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert } convert && convert.Type == typeof(object)
            ? convert.Operand
            : expression;

    private ArgumentException Unsupported(Expression expression) =>
        new($"Unsupported expression in a PQS filter: {expression.NodeType} '{expression}'. " +
            "Supported forms are Daml field access (nested records included), Optional, List, Map and " +
            "variant navigation, comparisons against a captured value, and && / || / !.",
            paramName);

    private sealed class ScopeReferenceFinder(Dictionary<ParameterExpression, Cursor> scopes) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= scopes.ContainsKey(node);
            return node;
        }
    }

    private sealed record Cursor(PqsPath Path, Type ClrType)
    {
        public bool InOptionalChain { get; private init; }

        public bool DefaultsWhenAbsent { get; private init; }

        public bool IsListEncodedOptional =>
            OptionalElementType(ClrType) is { } element && (InOptionalChain || OptionalElementType(element) is not null);

        public static Cursor Root(PqsScope scope, Type clrType) => new(new PqsScopeRoot(scope), clrType);

        public static Type? OptionalElementType(Type type)
        {
            for (var candidate = type; candidate is not null; candidate = candidate.BaseType)
            {
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Optional<>))
                    return candidate.GetGenericArguments()[0];
            }

            return null;
        }

        public Cursor Field(string name, Type clrType) => new(new PqsField(Path, name), clrType);

        public Cursor OptionalValue(Type clrType) =>
            IsListEncodedOptional ? new(new PqsFirstElement(Path), clrType) { InOptionalChain = true } : As(clrType);

        public Cursor DefaultingWhenAbsent() => this with { DefaultsWhenAbsent = true };

        public Cursor SomeCast(Type someType) =>
            this with { Path = new PqsSomeCast(Path, IsListEncodedOptional), ClrType = someType };

        public Cursor ConstructorCast(string tag, Type constructorType) =>
            this with { Path = new PqsConstructorCast(Path, tag), ClrType = constructorType };

        public Cursor MapEntry(PqsOperand key, int aliasOrdinal, Type valueType) =>
            new(new PqsMapEntry(Path, key, aliasOrdinal), valueType);

        public Cursor As(Type clrType) => this with { ClrType = clrType };
    }
}
