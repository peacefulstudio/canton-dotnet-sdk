// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Com.DigitalAsset.Daml.Lf.Archive.DamlLf2;

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.DarParser;

/// <summary>
/// Statically analyzes a Daml-LF expression tree typed at <c>List Party</c>
/// (the shape carried by template <c>signatories</c> / <c>observers</c> and
/// choice <c>controllers</c> / <c>observers</c>) and tries to resolve it to
/// an ordered list of payload-field references — the bread-and-butter case
/// where the user wrote, e.g., <c>signatory platform, initiator, counterparty</c>
/// against template fields of the same name.
///
/// <para>
/// The analyzer recognizes the LF emission pattern Daml uses for that surface
/// syntax: a <c>Cons(...)</c> chain whose head expressions are
/// <c>RecProj(template_param, fieldName)</c> nodes (or interned aliases for
/// those nodes) and whose tail terminates in a <c>Nil</c>. Anything else —
/// function calls, list helpers, variable references, key projections —
/// short-circuits to <see cref="DamlPartyAnalysis.Dynamic"/>, which the codegen
/// surfaces as a single explicit submitter parameter.
/// </para>
///
/// <para>
/// We deliberately do <em>not</em> attempt symbolic execution here — it would
/// trade a clear "I can't tell" answer for false positives. The set of accepted
/// shapes can grow over time (e.g. handling <c>signatory key._1</c>) without
/// invalidating the existing payload-field path.
/// </para>
/// </summary>
internal static class PartyExpressionAnalyzer
{
    /// <summary>
    /// A party-expression walk stops once its depth passes this, and an application
    /// spine stops once its link count does. The walk descends through
    /// interned-expression references and top-level value unfoldings, and the spine
    /// walks follow <c>App</c> / <c>TyApp</c> links through those same references; none
    /// of these the proto parser's own nesting limit constrains: all run through flat
    /// tables a hand-crafted DALF can chain to any length without nesting a message.
    /// Passing the bound resolves to <see cref="DamlPartyAnalysis.Dynamic"/>, the same
    /// verdict every other unsupported shape gets; the sibling type walk in this
    /// assembly throws on its own equal-valued bound instead, because an unanalyzable
    /// clause has a correct answer to fall back to and an unconvertible type does not.
    /// The two bounds are independent so that retuning one cannot move the other, and
    /// this one sits far above any clause damlc emits.
    /// </summary>
    internal const int MaxExpressionDepth = 256;

    /// <summary>
    /// Resolves the party-expression tree rooted at <paramref name="expr"/> using
    /// the interning context of the package. <paramref name="templateParamStr"/>
    /// is the interned-string index of the bound template parameter (e.g. the
    /// <c>this</c> binder named in <see cref="DefTemplate.ParamInternedStr"/>);
    /// only <c>RecProj</c>s rooted at that variable count as payload fields.
    /// Returns <see cref="DamlPartyAnalysis.Dynamic"/> on any unsupported shape.
    /// </summary>
    public static DamlPartyAnalysis Analyze(Expr expr, int templateParamStr, PackageContext ctx)
    {
        var parties = new List<DamlPartyReference>();
        var visitedInternedExprs = new HashSet<int>();
        return TryCollect(expr, templateParamStr, ctx, parties, visitedInternedExprs, allowValueIndirection: true, depth: 0)
            ? DamlPartyAnalysis.Static(parties)
            : DamlPartyAnalysis.Dynamic;
    }

    private enum PartyBindingKind
    {
        Opaque,
        TemplateParam,
        PayloadField,
    }

    private readonly record struct PartyBinding(PartyBindingKind Kind, string? FieldName)
    {
        public static readonly PartyBinding Opaque = new(PartyBindingKind.Opaque, null);
        public static readonly PartyBinding TemplateParam = new(PartyBindingKind.TemplateParam, null);
        public static PartyBinding Field(string fieldName) => new(PartyBindingKind.PayloadField, fieldName);
    }

    /// <summary>
    /// Resolves the whole-list indirection this SDK compiles a
    /// <c>signatory &lt;field&gt;</c> / <c>observer &lt;field&gt;</c> /
    /// <c>controller &lt;field&gt;</c> clause into: an application of a synthesized
    /// value to the template parameter, carrying the template parameter alone at
    /// template level — <c>App(Val(&lt;self&gt;), [Var(this)])</c> — and the choice
    /// argument as well per choice —
    /// <c>App(Val(&lt;self&gt;), [Var(this), Var(arg)])</c> — whose defining
    /// expression is <c>\this -> let ds = this.&lt;field&gt; in \arg -> toParties ds</c>.
    /// The defining expression resolves either by reducing to an imported
    /// single-<c>Party</c> list wrapper applied to a bound template payload field,
    /// or — at the template-level arity only, where no choice argument can have
    /// leaked into the list — by being a literal list of payload-field
    /// projections carrying no further value indirection. Every other shape stays
    /// <see cref="DamlPartyAnalysis.Dynamic"/>.
    ///
    /// <para>
    /// Two names are matched. The class selector <c>$$ctoParties</c> damlc
    /// synthesizes for the single-<c>Party</c> instance is that wrapper, typed
    /// <c>Party -&gt; [Party]</c>. The surface <c>toParties</c> is not: it is the
    /// <c>ToParties</c> class method, typed
    /// <c>forall a. &lt;dict&gt; -&gt; a -&gt; [Party]</c>. Matching its name is
    /// nonetheless safe, because a class-method application carries the dictionary
    /// as its first argument, so <see cref="FlattenApplication"/> yields two
    /// arguments and the single-argument check rejects the call before the name is
    /// consulted. The numbered selectors <c>$$ctoParties1</c> and
    /// <c>$$ctoParties2</c> are different functions taking <c>[Party]</c> and
    /// <c>Optional Party</c>, so resolving through them would name a field of the
    /// wrong type as a single party; they are matched by neither name.
    /// </para>
    /// </summary>
    private static bool TryResolvePartyValueIndirection(
        Expr expr,
        int templateParamStr,
        PackageContext ctx,
        List<DamlPartyReference> parties,
        HashSet<int> visitedInternedExprs,
        int depth)
    {
        if (depth > MaxExpressionDepth)
        {
            return false;
        }

        var args = new List<Expr>();
        var head = FlattenApplication(expr, args, ctx);
        if (head is null || args.Count == 0)
        {
            return false;
        }

        var templateParamArg = ResolveInterned(args[0], ctx);
        if (templateParamArg is null
            || templateParamArg.SumCase != Expr.SumOneofCase.VarInternedStr
            || templateParamArg.VarInternedStr != templateParamStr)
        {
            return false;
        }

        var callee = UnwrapTypeApplications(head, ctx);
        if (callee is null || callee.SumCase != Expr.SumOneofCase.Val)
        {
            return false;
        }

        var resolved = ResolveInterned(ctx.GetValueExpr(callee.Val), ctx);
        if (resolved is null || resolved.SumCase != Expr.SumOneofCase.Abs || resolved.Abs.Param.Count < 1 || resolved.Abs.Body is null)
        {
            return false;
        }

        var environment = new Dictionary<int, PartyBinding>();
        for (var i = 0; i < resolved.Abs.Param.Count; i++)
        {
            environment[resolved.Abs.Param[i].VarInternedStr] =
                i == 0 ? PartyBinding.TemplateParam : PartyBinding.Opaque;
        }

        var unfoldedValues = new HashSet<(int Module, int Name)> { ValueKey(callee.Val) };
        if (TryReduceSingletonPartyField(resolved.Abs.Body, environment, ctx, visitedInternedExprs, unfoldedValues, depth + 1, out var fieldName))
        {
            parties.Add(new DamlPartyPayloadField(fieldName));
            return true;
        }

        var isTemplateLevelIndirection = args.Count == 1 && resolved.Abs.Param.Count == 1;
        return isTemplateLevelIndirection
            && TryCollect(
                resolved.Abs.Body,
                resolved.Abs.Param[0].VarInternedStr,
                ctx,
                parties,
                visitedInternedExprs,
                allowValueIndirection: false,
                depth + 1);
    }

    /// <summary>
    /// Walks the <c>Fun</c> spine of a possibly-curried application, accumulating
    /// its arguments left to right, and returns the applied expression. Daml-LF
    /// admits both <c>App(f, [a, b])</c> and <c>App(App(f, [a]), [b])</c> for the
    /// same source-level call, so an analyzer that reads <c>Args</c> off the
    /// outermost node alone sees an arity that depends on how the compiler chose
    /// to nest, not on the call it is looking at. Arguments are appended walking
    /// outermost-first and the appended range reversed once at the end, so a long
    /// chain costs linear work. A spine longer than <see cref="MaxExpressionDepth"/>
    /// ends the walk unresolved: the interned-reference guard only stops cycles, and
    /// an acyclic spine is otherwise bounded by nothing but the DALF's size.
    /// </summary>
    private static Expr? FlattenApplication(Expr expr, List<Expr> args, PackageContext ctx)
    {
        var appendedFrom = args.Count;
        var dereferencedInternedExprs = new HashSet<int>();
        var current = ResolveInterned(expr, ctx, dereferencedInternedExprs);
        var links = 0;
        while (current is not null && current.SumCase == Expr.SumOneofCase.App)
        {
            if (++links > MaxExpressionDepth)
            {
                args.RemoveRange(appendedFrom, args.Count - appendedFrom);
                return null;
            }

            for (var i = current.App.Args.Count - 1; i >= 0; i--)
            {
                args.Add(current.App.Args[i]);
            }

            current = ResolveInterned(current.App.Fun, ctx, dereferencedInternedExprs);
        }

        args.Reverse(appendedFrom, args.Count - appendedFrom);
        return current;
    }

    /// <summary>
    /// Follows a chain of type applications to the underlying expression. Counts its
    /// links against the same <see cref="MaxExpressionDepth"/> bound
    /// <see cref="FlattenApplication"/> uses, on its own counter: a chain longer than
    /// that ends the walk unresolved, because only cycles are stopped by the
    /// interned-reference guard.
    /// </summary>
    private static Expr? UnwrapTypeApplications(Expr? expr, PackageContext ctx)
    {
        var dereferencedInternedExprs = new HashSet<int>();
        var current = ResolveInterned(expr, ctx, dereferencedInternedExprs);
        var links = 0;
        while (current is not null && current.SumCase == Expr.SumOneofCase.TyApp)
        {
            if (++links > MaxExpressionDepth)
            {
                return null;
            }

            current = ResolveInterned(current.TyApp.Expr, ctx, dereferencedInternedExprs);
        }

        return current;
    }

    private static Expr? ResolveInterned(Expr? expr, PackageContext ctx) =>
        ResolveInterned(expr, ctx, new HashSet<int>());

    /// <summary>
    /// Follows a chain of interned-expression references to the first node that
    /// is not one. <paramref name="dereferencedInternedExprs"/> accumulates the
    /// indexes already followed on this chain: a hand-crafted DALF can point an
    /// interned index back at one of its own ancestors, and following it again
    /// would never terminate. A revisited index ends the chain unresolved.
    /// </summary>
    private static Expr? ResolveInterned(Expr? expr, PackageContext ctx, HashSet<int> dereferencedInternedExprs)
    {
        var current = expr;
        while (current is not null && current.SumCase == Expr.SumOneofCase.InternedExpr)
        {
            if (!dereferencedInternedExprs.Add(current.InternedExpr))
            {
                return null;
            }

            current = ctx.GetInternedExpr(current.InternedExpr);
        }

        return current;
    }

    private static (int Module, int Name) ValueKey(ValueId value) =>
        (value.Module.ModuleNameInternedDname, value.NameInternedDname);

    /// <summary>
    /// Reduces an expression the analyzer expects to yield a single-element
    /// <c>List Party</c> to the payload field that element projects, walking
    /// <c>Let</c> and <c>Abs</c> binders and unfolding same-package value
    /// applications. <paramref name="unfoldedValues"/> holds the values already
    /// unfolded on this path so a value that applies itself, directly or through
    /// a mutual chain, reduces to no field instead of spinning.
    /// </summary>
    private static bool TryReduceSingletonPartyField(
        Expr expr,
        Dictionary<int, PartyBinding> environment,
        PackageContext ctx,
        HashSet<int> visitedInternedExprs,
        HashSet<(int Module, int Name)> unfoldedValues,
        int depth,
        out string fieldName)
    {
        fieldName = string.Empty;

        if (depth > MaxExpressionDepth)
        {
            return false;
        }

        if (expr.SumCase == Expr.SumOneofCase.InternedExpr)
        {
            if (!visitedInternedExprs.Add(expr.InternedExpr))
            {
                return false;
            }

            try
            {
                var resolved = ctx.GetInternedExpr(expr.InternedExpr);
                return resolved is not null
                    && TryReduceSingletonPartyField(resolved, environment, ctx, visitedInternedExprs, unfoldedValues, depth + 1, out fieldName);
            }
            finally
            {
                visitedInternedExprs.Remove(expr.InternedExpr);
            }
        }

        if (expr.SumCase == Expr.SumOneofCase.Let)
        {
            if (expr.Let.Body is null)
            {
                return false;
            }

            var boundBinders = new List<int>();
            foreach (var binding in expr.Let.Bindings)
            {
                if (binding.Bound is null)
                {
                    RemoveBindings(environment, boundBinders);
                    return false;
                }
                environment[binding.Binder.VarInternedStr] = ResolvePartyBinding(binding.Bound, environment, ctx);
                boundBinders.Add(binding.Binder.VarInternedStr);
            }

            try
            {
                return TryReduceSingletonPartyField(expr.Let.Body, environment, ctx, visitedInternedExprs, unfoldedValues, depth + 1, out fieldName);
            }
            finally
            {
                RemoveBindings(environment, boundBinders);
            }
        }

        if (expr.SumCase == Expr.SumOneofCase.Abs)
        {
            if (expr.Abs.Body is null)
            {
                return false;
            }

            var boundBinders = new List<int>();
            foreach (var param in expr.Abs.Param)
            {
                environment[param.VarInternedStr] = PartyBinding.Opaque;
                boundBinders.Add(param.VarInternedStr);
            }

            try
            {
                return TryReduceSingletonPartyField(expr.Abs.Body, environment, ctx, visitedInternedExprs, unfoldedValues, depth + 1, out fieldName);
            }
            finally
            {
                RemoveBindings(environment, boundBinders);
            }
        }

        if (expr.SumCase == Expr.SumOneofCase.App)
        {
            var args = new List<Expr>();
            var head = FlattenApplication(expr, args, ctx);
            if (head is null || args.Count != 1)
            {
                return false;
            }

            var callee = UnwrapTypeApplications(head, ctx);
            if (callee is null || callee.SumCase != Expr.SumOneofCase.Val)
            {
                return false;
            }

            var argumentBinding = ResolvePartyBinding(args[0], environment, ctx);
            if (IsImportedToParties(callee.Val, ctx))
            {
                if (argumentBinding.Kind != PartyBindingKind.PayloadField)
                {
                    return false;
                }

                fieldName = argumentBinding.FieldName!;
                return true;
            }

            var definition = ResolveInterned(ctx.GetValueExpr(callee.Val), ctx);
            if (definition is null
                || definition.SumCase != Expr.SumOneofCase.Abs
                || definition.Abs.Param.Count < 1
                || definition.Abs.Body is null
                || !unfoldedValues.Add(ValueKey(callee.Val)))
            {
                return false;
            }

            var unfoldedEnvironment = new Dictionary<int, PartyBinding>();
            for (var i = 0; i < definition.Abs.Param.Count; i++)
            {
                unfoldedEnvironment[definition.Abs.Param[i].VarInternedStr] =
                    i == 0 ? argumentBinding : PartyBinding.Opaque;
            }

            return TryReduceSingletonPartyField(
                definition.Abs.Body, unfoldedEnvironment, ctx, visitedInternedExprs, unfoldedValues, depth + 1, out fieldName);
        }

        return false;
    }

    private static void RemoveBindings(Dictionary<int, PartyBinding> environment, List<int> binders)
    {
        foreach (var binder in binders)
        {
            environment.Remove(binder);
        }
    }

    private static PartyBinding ResolvePartyBinding(
        Expr expr,
        Dictionary<int, PartyBinding> environment,
        PackageContext ctx)
    {
        var resolved = ResolveInterned(expr, ctx);
        if (resolved is null)
        {
            return PartyBinding.Opaque;
        }

        if (resolved.SumCase == Expr.SumOneofCase.VarInternedStr)
        {
            return environment.TryGetValue(resolved.VarInternedStr, out var binding) ? binding : PartyBinding.Opaque;
        }

        if (resolved.SumCase == Expr.SumOneofCase.RecProj)
        {
            var record = ResolveInterned(resolved.RecProj.Record, ctx);
            if (record is null)
            {
                return PartyBinding.Opaque;
            }

            if (record.SumCase == Expr.SumOneofCase.VarInternedStr
                && environment.TryGetValue(record.VarInternedStr, out var recordBinding)
                && recordBinding.Kind == PartyBindingKind.TemplateParam
                && ctx.TryGetString(resolved.RecProj.FieldInternedStr, out var fieldName))
            {
                return PartyBinding.Field(fieldName);
            }
        }

        return PartyBinding.Opaque;
    }

    private static readonly HashSet<string> SinglePartyToPartiesNames = new(StringComparer.Ordinal)
    {
        "toParties",
        "$$ctoParties",
    };

    private static bool IsImportedToParties(ValueId value, PackageContext ctx) =>
        IsImportedPackage(value.Module.PackageId)
        && ctx.GetDottedName(value.Module.ModuleNameInternedDname) == "DA.Internal.Template.Functions"
        && SinglePartyToPartiesNames.Contains(ctx.GetDottedName(value.NameInternedDname));

    /// <summary>
    /// Whether a package reference names a package other than the one being
    /// analyzed. Daml-LF spells that two ways: an interned package-id string up
    /// to 2.1, and an index into the package-import table from 2.2 on.
    /// </summary>
    private static bool IsImportedPackage(SelfOrImportedPackageId packageId) =>
        packageId.SumCase is SelfOrImportedPackageId.SumOneofCase.ImportedPackageIdInternedStr
            or SelfOrImportedPackageId.SumOneofCase.PackageImportId;

    /// <summary>
    /// Walks a <c>List Party</c>-typed expression, accumulating payload-field
    /// references in declaration order. Returns false on any element that the
    /// analyzer doesn't recognize as a static payload-field reference.
    /// </summary>
    /// <param name="expr">The expression to walk.</param>
    /// <param name="templateParamStr">Interned-string index of the bound template parameter.</param>
    /// <param name="ctx">Interning context of the package the expression belongs to.</param>
    /// <param name="parties">Accumulator receiving the resolved references in declaration order.</param>
    /// <param name="visitedInternedExprs">Interned-expression indexes on the current dereference path.</param>
    /// <param name="allowValueIndirection">
    /// Whether a top-level function-application shape may still be dereferenced at this point in
    /// the walk. True everywhere except inside the body of an already-dereferenced value — the
    /// dictionary-method indirection this analyzer resolves is one level only, so a resolved body
    /// that itself applies another top-level value must bail to Dynamic rather than chase the
    /// chain further.
    /// </param>
    /// <param name="depth">
    /// Number of analyzer frames already entered on this walk. A walk that passes
    /// <see cref="MaxExpressionDepth"/> stops and reports the shape as unsupported.
    /// </param>
    /// <remarks>
    /// Four expression shapes are recognized.
    /// <list type="number">
    /// <item><c>Nil</c> — the terminator of a <c>Cons</c> chain.</item>
    /// <item>
    /// <c>Cons</c> — <c>Front</c> holds the head expressions in declaration order and <c>Tail</c>
    /// the rest of the list. A well-formed chain terminates in a <c>Nil</c> or another
    /// <c>Cons</c>, so a missing <c>Tail</c> is a partial AST: reporting Static off it would emit
    /// named-signatory wrappers against a shape that was never verified.
    /// </item>
    /// <item>
    /// An interned expression, which only appears from Daml-LF 2.2 on. A hand-crafted DALF can
    /// point an interned index back at an ancestor of the current dereference path, directly or
    /// through a mutual chain; dereferencing it again would recurse until the process dies of an
    /// uncatchable <see cref="StackOverflowException"/>, so a revisited index is treated as an
    /// unsupported shape.
    /// </item>
    /// <item>
    /// Function application through a same-package top-level value — the shape the SDK's
    /// generic-template desugaring emits for an otherwise ordinary <c>signatory dso</c> clause,
    /// a synthesized dictionary method applied to the template's bound parameter as
    /// <c>App(Val(&lt;self&gt;, "$$csignatory"), [Var(this)])</c>, and for a
    /// <c>controller &lt;field&gt;</c> clause, which additionally passes the choice argument.
    /// <see cref="TryResolvePartyValueIndirection"/> resolves both.
    /// </item>
    /// </list>
    /// Anything else — a <c>Var</c> calling a helper, a <c>Case</c> — bails out.
    /// </remarks>
    private static bool TryCollect(
        Expr expr,
        int templateParamStr,
        PackageContext ctx,
        List<DamlPartyReference> parties,
        HashSet<int> visitedInternedExprs,
        bool allowValueIndirection,
        int depth)
    {
        if (depth > MaxExpressionDepth)
        {
            return false;
        }

        if (expr.SumCase == Expr.SumOneofCase.Nil)
        {
            return true;
        }

        if (expr.SumCase == Expr.SumOneofCase.Cons)
        {
            foreach (var head in expr.Cons.Front)
            {
                if (!TryResolveSingleParty(head, templateParamStr, ctx, parties, visitedInternedExprs, depth + 1))
                {
                    return false;
                }
            }

            if (expr.Cons.Tail is null)
            {
                return false;
            }

            return TryCollect(expr.Cons.Tail, templateParamStr, ctx, parties, visitedInternedExprs, allowValueIndirection, depth + 1);
        }

        if (expr.SumCase == Expr.SumOneofCase.InternedExpr)
        {
            if (!visitedInternedExprs.Add(expr.InternedExpr))
            {
                return false;
            }

            try
            {
                var resolved = ctx.GetInternedExpr(expr.InternedExpr);
                return resolved is not null && TryCollect(resolved, templateParamStr, ctx, parties, visitedInternedExprs, allowValueIndirection, depth + 1);
            }
            finally
            {
                visitedInternedExprs.Remove(expr.InternedExpr);
            }
        }

        if (allowValueIndirection && expr.SumCase == Expr.SumOneofCase.App)
        {
            return TryResolvePartyValueIndirection(expr, templateParamStr, ctx, parties, visitedInternedExprs, depth + 1);
        }

        return false;
    }

    /// <summary>
    /// Tries to resolve a single <c>Party</c>-typed expression to a
    /// <see cref="DamlPartyReference"/>. The only currently-supported shape is
    /// a record projection on the template parameter — that's the
    /// <c>signatory platform</c> case.
    /// </summary>
    /// <remarks>
    /// The projected record must be a bare <c>Var</c> bound to the template parameter, though
    /// either the projection or the record itself may arrive interned. A projection through any
    /// other binder — a let binding, a nested record, a function argument — would require codegen
    /// to invent an expression tree to derive the <c>actAs</c> set, which is exactly what the
    /// Dynamic verdict exists to surface. An out-of-range field index bails out too: it resolves to
    /// no name at all, and recording the placeholder one reads as would name a payload field the
    /// template never declares. Revisited interned indexes bail out for the same reason
    /// as in <see cref="TryCollect"/>: a self- or mutually-referential index would otherwise
    /// recurse until the process dies of an uncatchable <see cref="StackOverflowException"/>.
    /// </remarks>
    private static bool TryResolveSingleParty(
        Expr expr,
        int templateParamStr,
        PackageContext ctx,
        List<DamlPartyReference> parties,
        HashSet<int> visitedInternedExprs,
        int depth)
    {
        if (depth > MaxExpressionDepth)
        {
            return false;
        }

        if (expr.SumCase == Expr.SumOneofCase.InternedExpr)
        {
            if (!visitedInternedExprs.Add(expr.InternedExpr))
            {
                return false;
            }

            try
            {
                var resolved = ctx.GetInternedExpr(expr.InternedExpr);
                return resolved is not null && TryResolveSingleParty(resolved, templateParamStr, ctx, parties, visitedInternedExprs, depth + 1);
            }
            finally
            {
                visitedInternedExprs.Remove(expr.InternedExpr);
            }
        }

        if (expr.SumCase == Expr.SumOneofCase.RecProj)
        {
            var recordExpr = ResolveInterned(expr.RecProj.Record, ctx);
            if (recordExpr is null)
            {
                return false;
            }

            if (recordExpr.SumCase != Expr.SumOneofCase.VarInternedStr)
            {
                return false;
            }

            if (recordExpr.VarInternedStr != templateParamStr)
            {
                return false;
            }

            if (!ctx.TryGetString(expr.RecProj.FieldInternedStr, out var fieldName))
            {
                return false;
            }

            parties.Add(new DamlPartyPayloadField(fieldName));
            return true;
        }

        return false;
    }
}
