// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterByKeyExerciserTests
{
    private const string LocalPackageId = "pkg-id";

    private static readonly DamlPrimitiveType Unit = new(DamlPrimitive.Unit);

    private static readonly DamlPrimitiveType Int64 = new(DamlPrimitive.Int64);

    private static readonly IReadOnlyList<DamlFieldDefinition> VaultFields =
        [
            new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party)),
            new DamlFieldDefinition("agent", new DamlPrimitiveType(DamlPrimitive.Party)),
        ];

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private static DamlChoice Choice(
        string name,
        DamlType returnType,
        DamlPartyAnalysis? controllers = null,
        DamlType? argumentType = null) =>
        new()
        {
            Name = name,
            ArgumentType = argumentType ?? Unit,
            ReturnType = returnType,
            Consuming = false,
            Controllers = controllers ?? DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTemplate Template(DamlType? key, params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Choices = choices,
            Key = key,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static readonly DamlTypeRef VaultKey = new(LocalPackageId, "Main", "VaultKey");

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    private static string EmitRecordBody(DamlTemplate template)
    {
        var module = new DamlModule
        {
            Name = "Main",
            Templates = [template],
            DataTypes = [],
            Interfaces = [],
        };
        var package = new DamlPackage
        {
            PackageId = LocalPackageId,
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.3",
            Modules = [module],
            DependencyReferences = [],
        };
        var options = new CodeGenOptions { NamespacePrefix = "Test.Package" };
        var resolution = RealResolution.Of(package, options);
        var context = resolution.Context;
        var resolver = resolution.Resolver;
        var mapper = new DamlTypeMapper(context, resolver);
        var party = new PartyAnalysis();
        var emitter = new TemplateEmitter(
            context,
            resolver,
            new RecordSerializationEmitter(context, resolver, options, mapper),
            new ChoiceEmitter(context, resolver, options, mapper, party),
            new SubmissionExtensionsEmitter(options, party),
            options);

        var emitted = new StringBuilder();
        emitter.WriteTemplateType(new IndentWriter(emitted), package, module, template, VaultFields);

        var lines = emitted.ToString().ReplaceLineEndings("\n").Split('\n');
        var recordClose = Array.IndexOf(lines, "}");
        recordClose.Should().BePositive("the template record is the first type emitted and closes at column 0");
        return string.Join('\n', lines[..recordClose]);
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_keyed_template_gets_a_static_exerciser_beside_the_by_key_command()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64)));

        body.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseByKeyCommand PeekByKeyCommand(");
        body.Should().Contain(
            "public static async global::System.Threading.Tasks.Task<global::Daml.Runtime.Outcomes.ExerciseOutcome<"
            + "global::Daml.Runtime.Outcomes.ByKeyExercise<global::Test.Package.Main.Vault, long>>> TryPeekByKeyAsync(");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_takes_the_client_then_the_key_then_the_submitter_then_the_submission_tail()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64)));

        body.Should().ContainAll(
            "global::Daml.Ledger.Abstractions.ILedgerWriter client,",
            "global::Test.Package.Main.VaultKey key,",
            "global::Daml.Runtime.Commands.SubmitterInfo submitter,",
            "string? workflowId = null,",
            "global::Daml.Runtime.Commands.CommandId? commandId = null,",
            "global::System.TimeSpan? timeout = null,",
            "global::System.Func<global::Daml.Runtime.Commands.CommandsSubmission, global::Daml.Runtime.Commands.CommandsSubmission>? configure = null,",
            "global::System.Threading.CancellationToken cancellationToken = default)");
        body.IndexOf("ILedgerWriter client,", StringComparison.Ordinal).Should()
            .BeLessThan(body.IndexOf("VaultKey key,", StringComparison.Ordinal));
        body.IndexOf("VaultKey key,", StringComparison.Ordinal).Should()
            .BeLessThan(body.IndexOf("SubmitterInfo submitter,", StringComparison.Ordinal));
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_submits_the_by_key_command_and_projects_the_root_exercise()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64)));

        body.Should().Contain("var command = PeekByKeyCommand(key);");
        body.Should().Contain(
            "var outcome = await client.TrySubmitSingleAsync(command, submitter, workflowId, commandId, timeout, configure, cancellationToken).ConfigureAwait(false);");
        body.Should().Contain("return outcome.ProjectCommitted(tx => tx.ProjectByKeyExercise(global::Test.Package.Main.Vault.ChoicePeek));");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_passes_a_record_argument_to_the_command_builder()
    {
        var argument = new DamlTypeRef(LocalPackageId, "Main", "Vault.Credit");
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64, argumentType: argument)));

        body.Should().Contain("var command = PeekByKeyCommand(key, argument);");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_a_dynamic_controller_choice_gets_only_the_submitter_info_overload()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64)));

        CountOf(body, ">> TryPeekByKeyAsync(").Should().Be(1);
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_a_static_controller_choice_also_gets_a_named_party_overload()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64, StaticParties("owner"))));

        CountOf(body, ">> TryPeekByKeyAsync(").Should().Be(2);
        body.Should().Contain("global::Daml.Runtime.Data.Party owner,");
        body.Should().Contain("global::Daml.Runtime.Commands.SubmitterInfo submitter = owner;");
        body.Should().Contain(
            "return TryPeekByKeyAsync(client, key, submitter, workflowId, commandId, timeout, configure, cancellationToken);");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_unions_several_controllers_into_the_act_as_set_in_declaration_order()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64, StaticParties("owner", "agent"))));

        body.Should().Contain("global::Daml.Runtime.Data.Party owner,");
        body.Should().Contain("global::Daml.Runtime.Data.Party agent,");
        body.Should().Contain(
            "var submitter = new global::Daml.Runtime.Commands.SubmitterInfo("
            + "new global::System.Collections.Generic.HashSet<global::Daml.Runtime.Data.Party> { owner, agent });");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_carries_the_approved_remark_on_every_overload()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64, StaticParties("owner"))));

        const string RemarkText =
            "/// Contract keys are not unique: several active contracts may carry the same key, and the ledger resolves this command against a first match by an order it only partly guarantees. "
            + "The outcome's ResolvedContractId names the contract the ledger chose; to force the choice, disclose the intended contract through configure. "
            + "Keeping a key unique is the application's responsibility. "
            + "No active contract with the key is a DamlError with error id CONTRACT_KEY_NOT_FOUND.";
        var flattenedBody = string.Join(' ', body.Split('\n').Select(line => line.Trim()))
            .Replace("/// ", "", StringComparison.Ordinal);
        var flattenedRemark = RemarkText.Replace("/// ", "", StringComparison.Ordinal);

        CountOf(flattenedBody, flattenedRemark).Should().Be(2);
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_documents_the_key_parameter_and_the_shared_submission_parameters()
    {
        var body = EmitRecordBody(Template(VaultKey, Choice("Peek", Int64)));

        body.Should().Contain("/// <param name=\"client\">The ledger client.</param>");
        body.Should().Contain("/// <param name=\"key\">The contract key to exercise the choice against.</param>");
        body.Should().Contain("/// <param name=\"workflowId\">Optional workflow id;");
        body.Should().Contain("/// <param name=\"cancellationToken\">Cancellation token.</param>");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_is_absent_from_a_key_less_template()
    {
        var body = EmitRecordBody(Template(null, Choice("Peek", Int64, StaticParties("owner"))));

        body.Should().NotContain("ByKeyAsync");
        body.Should().NotContain("ByKeyExercise");
    }

    [Fact]
    public void ChoiceEmitterByKeyExerciser_covers_every_choice_of_the_template()
    {
        var body = EmitRecordBody(Template(
            VaultKey,
            Choice("Spawn", ContractIdOf("Vault")),
            Choice("Peek", Int64)));

        body.Should().Contain(" TrySpawnByKeyAsync(");
        body.Should().Contain(" TryPeekByKeyAsync(");
    }

    private static int CountOf(string text, string fragment)
    {
        var count = 0;
        for (var index = text.IndexOf(fragment, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
