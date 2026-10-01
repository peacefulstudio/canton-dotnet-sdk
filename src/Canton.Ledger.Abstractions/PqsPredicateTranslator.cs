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

    private static readonly SqlFragment PayloadColumn = SqlFragment.Of($"payload");
    private static readonly SqlFragment VariantTagKey = SqlFragment.JsonKey("tag");
    private static readonly SqlFragment VariantValueKey = SqlFragment.JsonKey("value");

    private readonly Dictionary<ParameterExpression, PayloadNode> scopes = [];
    private readonly string paramName;
    private int aliasCount;

    private PqsPredicateTranslator(ParameterExpression root, string paramName)
    {
        scopes[root] = PayloadNode.Root(PayloadColumn, root.Type);
        this.paramName = paramName;
    }

    public static SqlFragment FieldEquals(LambdaExpression selector, string value)
    {
        var translator = new PqsPredicateTranslator(selector.Parameters[0], nameof(selector));
        var node = UnwrapOptionals(translator.Node(StripBoxing(selector.Body)));
        var leaf = translator.LeafOf(node);
        return node.Apply(SqlFragment.Of($"{TypedValue(node, leaf)} = {leaf.ParseParameter(value, nameof(value))}"));
    }

    public static SqlFragment Where(LambdaExpression predicate) =>
        new PqsPredicateTranslator(predicate.Parameters[0], nameof(predicate)).Predicate(predicate.Body);

    private SqlFragment Predicate(Expression expression) =>
        expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } and =>
                SqlFragment.Of($"({Predicate(and.Left)} AND {Predicate(and.Right)})"),
            BinaryExpression { NodeType: ExpressionType.OrElse } or =>
                SqlFragment.Of($"({Predicate(or.Left)} OR {Predicate(or.Right)})"),
            UnaryExpression { NodeType: ExpressionType.Not } not =>
                SqlFragment.Of($"NOT COALESCE({Predicate(not.Operand)}, FALSE)"),
            BinaryExpression binary when ComparisonOperator(binary.NodeType) is not null => Comparison(binary),
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

    private SqlFragment Comparison(BinaryExpression binary)
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
            && PayloadNode.OptionalElementType(variant.Type) is null
            ? ConstructorAbsence(Node(variant), cast.Type)
            : Compare(Node(payloadSide), comparison, value);
    }

    private static SqlFragment ConstructorAbsence(PayloadNode variant, Type constructorType) =>
        variant.Apply(SqlFragment.Of($"{variant.Field(VariantTagKey, typeof(string)).Text} IS DISTINCT FROM {SqlFragment.Parameter(TagOf(constructorType))}"));

    private SqlFragment Compare(PayloadNode node, ExpressionType comparison, object? value)
    {
        if (value is null)
            return CompareWithNull(node, comparison);
        var leaf = LeafOf(node);
        if (!leaf.IsOrdered && comparison is not (ExpressionType.Equal or ExpressionType.NotEqual))
            throw new ArgumentException(
                $"A Daml {leaf.DamlTypeName} field only supports == and != in a PQS filter.", paramName);
        var sqlOperator = ComparisonOperator(comparison)!;
        return node.Apply(SqlFragment.Of($"{TypedValue(node, leaf)} {sqlOperator} {leaf.Parameter(value!)}"));
    }

    private static SqlFragment TypedValue(PayloadNode node, PqsLeafType leaf) =>
        node.DefaultsWhenAbsent
            ? SqlFragment.Of($"COALESCE({leaf.Typed(node.Text)}, {leaf.DefaultParameter(node.ClrType)})")
            : leaf.Typed(node.Text);

    private static PayloadNode UnwrapOptionals(PayloadNode node)
    {
        while (PayloadNode.OptionalElementType(node.ClrType) is { } element)
            node = node.OptionalValue(element);
        return node;
    }

    private PayloadNode OptionalRead(PayloadNode optional, MethodCallExpression call)
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

    private SqlFragment CompareWithNull(PayloadNode node, ExpressionType comparison)
    {
        if (comparison is not (ExpressionType.Equal or ExpressionType.NotEqual))
            throw new ArgumentException("A null value can be compared only with == and != in a PQS filter.", paramName);
        if (PayloadNode.OptionalElementType(node.ClrType) is not null)
            throw new ArgumentException(
                $"An Optional<T> field is never null; test it with .HasValue or an 'is Optional<T>.None' pattern instead.",
                paramName);
        return OptionalPresence(node, isSome: comparison == ExpressionType.NotEqual);
    }

    private static SqlFragment OptionalPresence(PayloadNode node, bool isSome) =>
        node.Apply(isSome ? node.IsSome() : node.IsNone());

    private SqlFragment TypeTest(TypeBinaryExpression typeIs)
    {
        var node = Node(typeIs.Expression);
        if (PayloadNode.OptionalElementType(typeIs.Expression.Type) is { } element)
        {
            if (typeIs.TypeOperand == typeof(Optional<>.Some).MakeGenericType(element))
                return OptionalPresence(node, isSome: true);
            if (typeIs.TypeOperand == typeof(Optional<>.None).MakeGenericType(element))
                return OptionalPresence(node, isSome: false);
        }

        if (IsVariantConstructor(typeIs.TypeOperand, typeIs.Expression.Type))
            return node.Apply(TagEquals(node, typeIs.TypeOperand));

        throw Unsupported(typeIs);
    }

    private SqlFragment ListPredicate(MethodCallExpression call)
    {
        var list = Node(call.Arguments[0]);
        var alias = SqlFragment.Alias('e', aliasCount++);
        var elements = SqlFragment.Of(
            $"SELECT 1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof({list.Json}) = 'array' THEN {list.Json} END) AS {alias}(value)");
        var element = PayloadNode.Root(SqlFragment.Of($"{alias}.value"), call.Method.GetGenericArguments()[0]);

        var predicate = call switch
        {
            { Method.Name: nameof(Enumerable.Any), Arguments.Count: 1 } => SqlFragment.Of($"EXISTS ({elements})"),
            { Method.Name: nameof(Enumerable.Any), Arguments: [_, LambdaExpression condition] } =>
                SqlFragment.Of($"EXISTS ({elements} WHERE {ElementPredicate(condition, element)})"),
            { Method.Name: nameof(Enumerable.All), Arguments: [_, LambdaExpression condition] } =>
                SqlFragment.Of($"NOT EXISTS ({elements} WHERE NOT COALESCE({ElementPredicate(condition, element)}, FALSE))"),
            { Method.Name: nameof(Enumerable.Contains), Arguments: [_, var item] } when !ReferencesScope(item) =>
                SqlFragment.Of($"EXISTS ({elements} WHERE {Compare(element, ExpressionType.Equal, Evaluate(item))})"),
            _ => throw Unsupported(call),
        };
        return list.Apply(predicate);
    }

    private SqlFragment ElementPredicate(LambdaExpression condition, PayloadNode element)
    {
        scopes[condition.Parameters[0]] = element;
        return Predicate(condition.Body);
    }

    private SqlFragment KeyPresence(PayloadNode map, Expression key) =>
        map.Apply(SqlFragment.Of($"{MapLookup(map, key).Json} IS NOT NULL"));

    private PayloadNode MapLookup(PayloadNode map, Expression keyExpression)
    {
        if (ReferencesScope(keyExpression))
            throw new ArgumentException(
                $"Unsupported Map lookup '{keyExpression}': the Map key must be a captured value, not a payload field.",
                paramName);
        var (keyType, valueType) = MapTypes(map.ClrType)!.Value;
        var key = Evaluate(keyExpression)!;
        var keyLeaf = PqsLeafType.For(keyType)
            ?? throw new ArgumentException($"A '{keyType.Name}' Map key is not supported in a PQS filter.", paramName);

        var alias = SqlFragment.Alias('m', aliasCount++);
        var genMapLookup = SqlFragment.Of(
            $"(SELECT {alias}.value->1 FROM jsonb_array_elements(CASE WHEN jsonb_typeof({map.Json}) = 'array' THEN {map.Json} END) " +
            $"AS {alias}(value) WHERE {keyLeaf.Typed(SqlFragment.Of($"{alias}.value->>0"))} = {keyLeaf.Parameter(key)} LIMIT 1)");
        var lookup = keyType == typeof(string)
            ? SqlFragment.Of($"COALESCE({map.Json}->{SqlFragment.Parameter(key)}, {genMapLookup})")
            : genMapLookup;
        return map.Derived(lookup, valueType);
    }

    private PqsLeafType LeafOf(PayloadNode node) =>
        PqsLeafType.For(node.ClrType)
        ?? throw new ArgumentException(
            $"'{node.ClrType.Name}' is not a Daml leaf type; select one of its fields instead.", paramName);

    private PayloadNode Node(Expression expression) =>
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

    private static PayloadNode Member(PayloadNode parent, MemberExpression member)
    {
        if (member.Member.Name == nameof(Nullable<int>.Value) && IsOptional(member.Expression!.Type))
            return parent.OptionalValue(member.Type);
        if (member.Member.Name == VariantValueProperty && IsVariantConstructor(member.Expression!.Type, member.Expression.Type.BaseType))
            return parent.Field(VariantValueKey, member.Type);
        if (IsTuple(member.Member.DeclaringType))
            return parent.Field(SqlFragment.JsonKey(member.Member.Name), member.Type);
        var attribute = member.Member.GetCustomAttribute<DamlFieldAttribute>()
            ?? throw new InvalidOperationException(
                $"Property '{member.Member.DeclaringType?.Name}.{member.Member.Name}' carries no [DamlField] metadata, so its " +
                $"PQS wire field name cannot be resolved. Regenerate the Daml bindings with a codegen " +
                $"that emits field-name metadata; the typed filter DSL reads the wire name from that " +
                $"attribute and never guesses from the C# property name.");
        return parent.Field(SqlFragment.JsonKey(attribute.Name), member.Type);
    }

    private static PayloadNode ConstructorCast(PayloadNode variant, Type constructorType) =>
        variant.Guarded(TagEquals(variant, constructorType)).As(constructorType);

    private static SqlFragment TagEquals(PayloadNode variant, Type constructorType) =>
        SqlFragment.Of($"{variant.Field(VariantTagKey, typeof(string)).Text} = {SqlFragment.Parameter(TagOf(constructorType))}");

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

    private static PayloadNode SomeCast(PayloadNode optional, Type someType) =>
        optional.Guarded(optional.IsSome()).As(someType);

    private static bool IsOptional(Type type) =>
        Nullable.GetUnderlyingType(type) is not null || PayloadNode.OptionalElementType(type) is not null;

    private static bool IsOptionalSome(Type target, Type source) =>
        PayloadNode.OptionalElementType(source) is { } element
        && target == typeof(Optional<>.Some).MakeGenericType(element);

    private static bool IsRepresentationPreserving(Type from, Type to) =>
        to == typeof(object)
        || Nullable.GetUnderlyingType(to) == from
        || (from.IsEnum && to == Enum.GetUnderlyingType(from));

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

    private static SqlFragment? ComparisonOperator(ExpressionType comparison) =>
        comparison switch
        {
            ExpressionType.Equal => SqlFragment.Of($"="),
            ExpressionType.NotEqual => SqlFragment.Of($"IS DISTINCT FROM"),
            ExpressionType.LessThan => SqlFragment.Of($"<"),
            ExpressionType.LessThanOrEqual => SqlFragment.Of($"<="),
            ExpressionType.GreaterThan => SqlFragment.Of($">"),
            ExpressionType.GreaterThanOrEqual => SqlFragment.Of($">="),
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

    private sealed class ScopeReferenceFinder(Dictionary<ParameterExpression, PayloadNode> scopes) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= scopes.ContainsKey(node);
            return node;
        }
    }
}
