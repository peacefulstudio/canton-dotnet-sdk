// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

internal sealed partial class ChoiceEmitter
{
    private string RequireAndMapReturnType(IndentWriter indent, DamlChoice choice)
    {
        StdlibPackages.RequireForFieldType(resolver, context.Package, indent, choice.ReturnType);
        return mapper.MapType(choice.ReturnType);
    }

    /// <summary>
    /// Emits the private <c>Project&lt;Choice&gt;Result</c> hand-off: it passes the choice's
    /// generated descriptor to the runtime's Exercise-result projection
    /// (<c>ExerciseOutcomeProjection.ProjectChoiceResult</c>), which locates the exercised event,
    /// decodes it through the descriptor's <c>ResultDecoder</c>, and owns the no-event diagnostic.
    /// </summary>
    private void WriteExerciseProjector(
        IndentWriter indent,
        DamlChoice choice,
        string templateClassName)
    {
        var choiceName = SanitizeIdentifier(choice.Name);
        var returnTypeName = mapper.MapType(choice.ReturnType);

        indent.AppendLine($"private static {TypeReferenceQualifier.Qualify(RuntimeTypeNames.ExerciseOutcome)}<{returnTypeName}> Project{choiceName}Result({TypeReferenceQualifier.Qualify(RuntimeTypeNames.TransactionResult)} tx, string contractId) =>");
        indent.Indent();
        indent.AppendLine($"tx.ProjectChoiceResult({templateClassName}.Choice{choiceName}, contractId);");
        indent.Dedent();
    }
}
