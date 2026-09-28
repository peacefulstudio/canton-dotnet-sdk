// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Codegen for the typed-submitter surface. Each template gets a
/// <c>&lt;TemplateName&gt;SubmissionExtensions</c> static class with:
///
/// <list type="bullet">
///   <item>One <c>TryCreateAsync</c> overload that takes the template payload (and
///   any extra parties that the analyzer flagged as not derivable from the
///   payload) and submits a Create command via
///   <c>ILedgerWriter.TryCreateAsync</c>. When every signatory is a payload
///   field reference (<c>signatory platform, initiator, counterparty</c> with
///   matching <c>Party</c> fields), the wrapper picks them off the payload
///   automatically — the caller never restates a party that's already in the
///   record.</item>
///   <item>An <c>Observers(payload)</c> documentation helper, when the
///   template's <c>observer</c> clause is statically resolvable. Returns the
///   ordered list of <see cref="Daml.Runtime.Data.Party"/> derived from the
///   payload — useful for inspecting / asserting the observer set without
///   exercising a choice.</item>
/// </list>
///
/// <para>
/// Constructed once per package over the package's
/// <see cref="PackageEmitContext"/> and the shared <see cref="PartyAnalysis"/>
/// module. Distinct from the choice-exercise emitters: creating a contract is a
/// different concern from exercising a choice.
/// </para>
///
/// <para>
/// When the analysis yields no payload field to derive from — either because
/// the <c>signatory</c> expression is anything other than a payload-field
/// projection on the template parameter, or because it resolves statically to an
/// empty set — codegen falls back to a single <c>SubmitterInfo submitter</c> parameter —
/// callers retain full control via <c>SubmitterInfo</c>'s implicit conversion
/// from a single <c>Party</c>, so the legacy single-party call site stays a
/// one-liner.
/// </para>
/// </summary>
internal sealed class SubmissionExtensionsEmitter(
    PackageEmitContext context,
    CodeGenOptions options,
    PartyAnalysis party)
{
    /// <summary>
    /// Emits the per-template <c>SubmissionExtensions</c> static class. Returns
    /// <c>true</c> when the class was emitted (we always emit it for templates
    /// that have at least one choice or whose Create surface could benefit
    /// from payload-derived parties). The caller doesn't actually use the
    /// return value today; it's kept for symmetry with sibling emitters.
    /// </summary>
    internal bool TryWriteSubmissionExtensions(
        IndentWriter indent,
        DamlTemplate template,
        IReadOnlyList<DamlFieldDefinition> fields)
    {
        var className = Identifiers.Sanitize(template.Name);

        var partyFields = fields
            .Where(f => f.Type is DamlPrimitiveType { Primitive: DamlPrimitive.Party })
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        var signatories = party.ValidatePayloadParties(template.Signatories, partyFields);
        var observers = party.ValidatePayloadParties(template.Observers, partyFields);

        EmittedUsings.RequireAsyncLedgerCallNamespaces(indent);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Typed-submitter extensions for <see cref=\"{className}\"/>. The");
            indent.AppendLine("/// <see cref=\"TryCreateAsync\"/> overload derives the <c>actAs</c> parties");
            indent.AppendLine("/// from the template payload (for signatories that are payload-field");
            indent.AppendLine("/// references) and from the caller's explicit arguments (for signatories");
            indent.AppendLine("/// the static analyzer could not resolve). When the template's");
            indent.AppendLine("/// <c>observer</c> clause is statically resolvable, an");
            indent.AppendLine("/// <c>Observers(payload)</c> documentation helper is also emitted.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {className}SubmissionExtensions");
        indent.AppendLine("{");
        indent.Indent();

        WriteTryCreateAsync(indent, template.Name, className, signatories);
        TryWriteObserversHelper(indent, template.Name, className, observers);

        indent.Dedent();
        indent.AppendLine("}");

        return true;
    }

    /// <summary>
    /// Emits the <c>TryCreateAsync</c> extension. On the static path, the payload
    /// alone is enough to derive every signatory — the caller passes only the
    /// payload and an optional cancellation token, and the wrapper builds a
    /// <c>SubmitterInfo</c> from the payload's <c>Party</c> properties. On the
    /// dynamic path, the caller passes a
    /// <c>SubmitterInfo</c> directly (with implicit conversion from
    /// a single <c>Party</c> for the single-party ergonomic).
    /// </summary>
    private void WriteTryCreateAsync(IndentWriter indent, string templateName, string className, DamlPartyAnalysis signatories)
    {
        var staticParties = signatories.Source == DamlPartySource.Static
                            && signatories.Parties.Count > 0;
        var multipleStatic = staticParties && signatories.Parties.Count > 1;

        if (multipleStatic)
        {
            indent.Require("System.Collections.Generic");
        }

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Creates a new <see cref=\"{className}\"/> contract on the ledger.");
            if (staticParties)
            {
                indent.AppendLine("/// The submitting parties are derived from the payload — each Daml signatory is");
                indent.AppendLine("/// a reference to a payload field, so the caller never restates a party that's");
                indent.AppendLine("/// already in the record.");
            }
            else if (signatories.Source == DamlPartySource.Static)
            {
                indent.AppendLine("/// The submitter is passed explicitly via <paramref name=\"submitter\"/>. The Daml");
                indent.AppendLine("/// <c>signatory</c> clause resolved to an empty set, so there is no payload party");
                indent.AppendLine("/// to derive. <see cref=\"SubmitterInfo\"/> implicitly converts from a single");
                indent.AppendLine("/// <c>Party</c>, so single-party callers still pass one literal.");
            }
            else
            {
                indent.AppendLine("/// The submitter is passed explicitly via <paramref name=\"submitter\"/>. The static");
                indent.AppendLine("/// analyzer could not resolve the Daml <c>signatory</c> clause to payload-field");
                indent.AppendLine("/// references — typically because the expression involves the template key, a");
                indent.AppendLine("/// constant, or a function call. <see cref=\"SubmitterInfo\"/> implicitly converts");
                indent.AppendLine("/// from a single <c>Party</c>, so single-party callers still pass one literal.");
            }
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            indent.AppendLine("/// <param name=\"payload\">The contract payload.</param>");
            if (!staticParties)
            {
                indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            }
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        indent.AppendLine($"public static Task<{context.Qualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{context.Qualifier.Qualify(RuntimeTypeNames.ContractId)}<{className}>>> TryCreateAsync(");
        indent.Indent();
        indent.AppendLine($"this {context.Qualifier.Qualify(RuntimeTypeNames.ILedgerWriter)} client,");
        indent.AppendLine($"{className} payload,");
        if (!staticParties)
        {
            indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter,");
        }
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();

        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(payload);");
        indent.AppendLine();

        if (staticParties)
        {
            if (multipleStatic)
            {
                indent.AppendLine($"var submitter = new {context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo)}(new {context.Qualifier.Qualify("HashSet")}<{context.Qualifier.Qualify(RuntimeTypeNames.Party)}>");
                indent.AppendLine("{");
                indent.Indent();
                for (var i = 0; i < signatories.Parties.Count; i++)
                {
                    if (signatories.Parties[i] is not DamlPartyPayloadField pf)
                    {
                        throw NotAPayloadField(templateName, "signatory", i, signatories.Parties[i]);
                    }

                    var prop = Identifiers.MemberName(pf.FieldName, className);
                    var comma = i < signatories.Parties.Count - 1 ? "," : "";
                    indent.AppendLine($"payload.{prop}{comma}");
                }
                indent.Dedent();
                indent.AppendLine("});");
            }
            else
            {
                if (signatories.Parties[0] is not DamlPartyPayloadField pf)
                {
                    throw NotAPayloadField(templateName, "signatory", 0, signatories.Parties[0]);
                }

                var prop = Identifiers.MemberName(pf.FieldName, className);
                indent.AppendLine($"{context.Qualifier.Qualify(RuntimeTypeNames.SubmitterInfo)} submitter = payload.{prop};");
            }
            indent.AppendLine();
        }

        indent.AppendLine($"return client.TryCreateAsync<{className}>(payload, submitter, cancellationToken: cancellationToken);");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Emits the per-template <c>Observers(payload)</c> documentation helper
    /// when the template's <c>observer</c> clause resolves statically to a
    /// non-empty set of payload-field references. Returns an
    /// <see cref="IReadOnlyList{Party}"/> derived from the payload's
    /// <c>Party</c> properties so callers can inspect the observer
    /// set directly.
    ///
    /// <para>
    /// Skipped when the analyzer's verdict is <see cref="DamlPartySource.Dynamic"/>
    /// (we can't know the answer at codegen time) or when it's a static empty
    /// list (the helper would always return <c>[]</c> — emitting it would be
    /// noise). Static-empty observers still register as a known-empty
    /// contribution to <c>readAs</c> on the emitted <c>Try&lt;Choice&gt;Async</c>
    /// wrappers, which is handled in <c>ChoiceResults.cs</c>.
    /// </para>
    /// </summary>
    private void TryWriteObserversHelper(IndentWriter indent, string templateName, string className, DamlPartyAnalysis observers)
    {
        if (observers.Source != DamlPartySource.Static || observers.Parties.Count == 0)
        {
            return;
        }

        indent.Require("System.Collections.Generic");
        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Returns the observers of a <see cref=\"{className}\"/> payload —");
            indent.AppendLine("/// the parties named in the Daml <c>observer</c> clause, derived from");
            indent.AppendLine("/// the payload's <c>Party</c> properties in declaration order. Useful");
            indent.AppendLine("/// for inspecting / asserting the observer set without exercising a");
            indent.AppendLine("/// choice.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"payload\">The contract payload.</param>");
        }

        indent.AppendLine($"public static {context.Qualifier.Qualify("IReadOnlyList")}<{context.Qualifier.Qualify(RuntimeTypeNames.Party)}> Observers({className} payload)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("ArgumentNullException.ThrowIfNull(payload);");
        indent.AppendLine($"return new {context.Qualifier.Qualify(RuntimeTypeNames.Party)}[]");
        indent.AppendLine("{");
        indent.Indent();
        for (var i = 0; i < observers.Parties.Count; i++)
        {
            if (observers.Parties[i] is not DamlPartyPayloadField pf)
            {
                throw NotAPayloadField(templateName, "observer", i, observers.Parties[i]);
            }

            var prop = Identifiers.MemberName(pf.FieldName, className);
            var comma = i < observers.Parties.Count - 1 ? "," : "";
            indent.AppendLine($"payload.{prop}{comma}");
        }
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
    }

    private static CodegenException NotAPayloadField(
        string templateName, string clause, int index, DamlPartyReference party) =>
        new($"Daml template {templateName} carries a static {clause} set whose element {index} is a "
            + $"{party.GetType().Name} reference rather than a payload field. ValidatePayloadParties demotes "
            + "an analysis holding any non-payload-field reference to Dynamic, so this emitter should never "
            + "reach one; skipping the element would derive a party set narrower than the clause names.");
}
