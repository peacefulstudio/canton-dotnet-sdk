// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>Writes the one skeleton every choice command builder shares.</summary>
internal static class ChoiceCommandBuilderWriter
{
    /// <summary>Writes <paramref name="builder"/> as a public static method.</summary>
    public static void Write(IndentWriter indent, ChoiceCommandBuilder builder)
    {
        if (builder.Docs is not null)
        {
            WriteDocs(indent, builder.Docs, builder.ArgumentParameterType is not null);
        }

        WriteSignature(indent, builder);
        indent.AppendLine("{");
        indent.Indent();
        foreach (var parameter in builder.NullGuardedParameters)
        {
            indent.AppendLine($"global::System.ArgumentNullException.ThrowIfNull({parameter});");
        }
        WriteReturnStatement(indent, builder.ReturnStatement);
        indent.Dedent();
        indent.AppendLine("}");
    }

    private static void WriteDocs(IndentWriter indent, CommandBuilderDocs docs, bool hasArgument)
    {
        indent.AppendLine("/// <summary>");
        foreach (var line in docs.Summary)
        {
            indent.AppendLine($"/// {line}");
        }
        indent.AppendLine("/// </summary>");
        indent.AppendLine($"/// {docs.ReceiverParameter}");
        if (hasArgument)
        {
            indent.AppendLine("/// <param name=\"argument\">The choice argument.</param>");
        }
        if (docs.Remarks.Count > 0)
        {
            indent.AppendLine("/// <remarks>");
            foreach (var line in docs.Remarks)
            {
                indent.AppendLine($"/// {line}");
            }
            indent.AppendLine("/// </remarks>");
        }
    }

    private static void WriteSignature(IndentWriter indent, ChoiceCommandBuilder builder)
    {
        indent.AppendLine($"public static {builder.CommandType} {builder.Name}(");
        indent.Indent();
        if (builder.ArgumentParameterType is null)
        {
            indent.AppendLine($"{builder.ReceiverParameter})");
        }
        else
        {
            indent.AppendLine($"{builder.ReceiverParameter},");
            indent.AppendLine($"{builder.ArgumentParameterType} argument)");
        }
        indent.Dedent();
    }

    private static void WriteReturnStatement(IndentWriter indent, IReadOnlyList<string> lines)
    {
        indent.AppendLine(lines[0]);
        indent.Indent();
        foreach (var line in lines.Skip(1))
        {
            indent.AppendLine(line);
        }
        indent.Dedent();
    }
}
