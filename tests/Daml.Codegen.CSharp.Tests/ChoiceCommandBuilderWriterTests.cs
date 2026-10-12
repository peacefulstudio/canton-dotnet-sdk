// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceCommandBuilderWriterTests
{
    private static string Render(ChoiceCommandBuilder builder)
    {
        var sb = new StringBuilder();
        ChoiceCommandBuilderWriter.Write(new IndentWriter(sb), builder);
        return sb.ToString();
    }

    [Fact]
    public void Write_renders_a_contract_id_builder_with_an_argument()
    {
        var builder = new ChoiceCommandBuilder(
            CommandType: "global::Daml.Runtime.Commands.ExerciseCommand",
            Name: "TransferCommand",
            Docs: new CommandBuilderDocs(
                Summary: ["Builds the command for the Transfer choice on this contract id."],
                ReceiverParameter: "<param name=\"contractId\">The contract on which to exercise the choice.</param>",
                Remarks: []),
            ReceiverParameter: "this global::Daml.Runtime.Contracts.ContractId<Vault> contractId",
            ArgumentParameterType: "Vault.Transfer",
            NullGuardedParameters: ["contractId", "argument"],
            ReturnStatement:
            [
                "return new global::Daml.Runtime.Commands.ExerciseCommand(",
                "Vault.TemplateId,",
                "contractId,",
                "new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"),",
                "argument.ToRecord());",
            ]);

        Render(builder).Should().Be(
            "/// <summary>\n" +
            "/// Builds the command for the Transfer choice on this contract id.\n" +
            "/// </summary>\n" +
            "/// <param name=\"contractId\">The contract on which to exercise the choice.</param>\n" +
            "/// <param name=\"argument\">The choice argument.</param>\n" +
            "public static global::Daml.Runtime.Commands.ExerciseCommand TransferCommand(\n" +
            "    this global::Daml.Runtime.Contracts.ContractId<Vault> contractId,\n" +
            "    Vault.Transfer argument)\n" +
            "{\n" +
            "    global::System.ArgumentNullException.ThrowIfNull(contractId);\n" +
            "    global::System.ArgumentNullException.ThrowIfNull(argument);\n" +
            "    return new global::Daml.Runtime.Commands.ExerciseCommand(\n" +
            "        Vault.TemplateId,\n" +
            "        contractId,\n" +
            "        new global::Daml.Runtime.Commands.ChoiceName(\"Transfer\"),\n" +
            "        argument.ToRecord());\n" +
            "}\n");
    }

    [Fact]
    public void Write_renders_remarks_and_a_plain_receiver_for_a_by_key_builder_without_an_argument()
    {
        var builder = new ChoiceCommandBuilder(
            CommandType: "global::Daml.Runtime.Commands.ExerciseByKeyCommand",
            Name: "ArchiveByKeyCommand",
            Docs: new CommandBuilderDocs(
                Summary: ["Builds the command for the Archive choice on the contract carrying this key."],
                ReceiverParameter: "<param name=\"key\">The contract key to exercise the choice against.</param>",
                Remarks: ["Keys are not unique.", "Keep them unique."]),
            ReceiverParameter: "VaultKey key",
            ArgumentParameterType: null,
            NullGuardedParameters: ["key"],
            ReturnStatement:
            [
                "return new global::Daml.Runtime.Commands.ExerciseByKeyCommand(",
                "Vault.TemplateId,",
                "key.ToRecord(),",
                "new global::Daml.Runtime.Commands.ChoiceName(\"Archive\"),",
                "global::Daml.Runtime.Data.DamlRecord.Create());",
            ]);

        Render(builder).Should().Be(
            "/// <summary>\n" +
            "/// Builds the command for the Archive choice on the contract carrying this key.\n" +
            "/// </summary>\n" +
            "/// <param name=\"key\">The contract key to exercise the choice against.</param>\n" +
            "/// <remarks>\n" +
            "/// Keys are not unique.\n" +
            "/// Keep them unique.\n" +
            "/// </remarks>\n" +
            "public static global::Daml.Runtime.Commands.ExerciseByKeyCommand ArchiveByKeyCommand(\n" +
            "    VaultKey key)\n" +
            "{\n" +
            "    global::System.ArgumentNullException.ThrowIfNull(key);\n" +
            "    return new global::Daml.Runtime.Commands.ExerciseByKeyCommand(\n" +
            "        Vault.TemplateId,\n" +
            "        key.ToRecord(),\n" +
            "        new global::Daml.Runtime.Commands.ChoiceName(\"Archive\"),\n" +
            "        global::Daml.Runtime.Data.DamlRecord.Create());\n" +
            "}\n");
    }

    [Fact]
    public void Write_omits_docs_and_guards_only_the_named_parameters_for_a_one_line_return()
    {
        var builder = new ChoiceCommandBuilder(
            CommandType: "global::Daml.Runtime.Commands.ExerciseCommand",
            Name: "DescribeCommand",
            Docs: null,
            ReceiverParameter: "this global::Daml.Runtime.Contracts.ContractId<IHolding> contractId",
            ArgumentParameterType: "long",
            NullGuardedParameters: ["contractId"],
            ReturnStatement: ["return global::Daml.Runtime.Commands.ExerciseCommand.For<IHolding>(contractId, choice, argument);"]);

        Render(builder).Should().Be(
            "public static global::Daml.Runtime.Commands.ExerciseCommand DescribeCommand(\n" +
            "    this global::Daml.Runtime.Contracts.ContractId<IHolding> contractId,\n" +
            "    long argument)\n" +
            "{\n" +
            "    global::System.ArgumentNullException.ThrowIfNull(contractId);\n" +
            "    return global::Daml.Runtime.Commands.ExerciseCommand.For<IHolding>(contractId, choice, argument);\n" +
            "}\n");
    }
}
