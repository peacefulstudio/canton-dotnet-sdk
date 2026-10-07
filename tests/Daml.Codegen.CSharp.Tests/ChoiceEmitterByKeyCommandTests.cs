// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ChoiceEmitterByKeyCommandTests
{
    private const string LocalPackageId = "pkg-id";

    private static DamlChoice Choice(string name, DamlType returnType) =>
        new()
        {
            Name = name,
            ArgumentType = new DamlPrimitiveType(DamlPrimitive.Unit),
            ReturnType = returnType,
            Consuming = false,
            Controllers = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static readonly IReadOnlyList<DamlFieldDefinition> VaultFields =
        [new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party))];

    private static DamlTemplate Template(DamlType? key, params DamlChoice[] choices) =>
        new()
        {
            Name = "Vault",
            Choices = choices,
            Key = key,
            Signatories = DamlPartyAnalysis.Dynamic,
            Observers = DamlPartyAnalysis.Dynamic,
        };

    private static DamlTypeApp ContractIdOf(string templateName) =>
        new(new DamlPrimitiveType(DamlPrimitive.ContractId), [new DamlTypeRef(LocalPackageId, "Main", templateName)]);

    /// <summary>
    /// The emitted template record body, and everything emitted beside it at namespace
    /// level — the choice-result structs and the <c>Extensions</c> /
    /// <c>NonContractExtensions</c> classes. The template record is the first type
    /// written, so it is the text up to the first line closing a type at column 0.
    /// </summary>
    private sealed record EmittedTemplate(string RecordBody, string SiblingTypes);

    private static EmittedTemplate Emit(DamlTemplate template)
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

        return new EmittedTemplate(
            string.Join('\n', lines[..recordClose]),
            string.Join('\n', lines[recordClose..]));
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_create_bearing_choice_gets_a_by_key_twin_of_its_command_builder()
    {
        var template = Template(new DamlTypeRef(LocalPackageId, "Main", "VaultKey"), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.SiblingTypes.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand SpawnCommand(");
        emitted.RecordBody.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseByKeyCommand SpawnByKeyCommand(");
        emitted.RecordBody.Should().Contain("global::Test.Package.Main.VaultKey key)");
        emitted.RecordBody.Should().Contain("key.ToRecord(),");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_value_returning_choice_gets_a_by_key_twin_of_its_command_builder()
    {
        var template = Template(new DamlTypeRef(LocalPackageId, "Main", "VaultKey"), Choice("Peek", new DamlPrimitiveType(DamlPrimitive.Int64)));

        var emitted = Emit(template);

        emitted.SiblingTypes.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand PeekCommand(");
        emitted.RecordBody.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseByKeyCommand PeekByKeyCommand(");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_lives_on_the_template_record_not_the_extensions_classes()
    {
        var template = Template(
            new DamlTypeRef(LocalPackageId, "Main", "VaultKey"),
            Choice("Spawn", ContractIdOf("Token")),
            Choice("Peek", new DamlPrimitiveType(DamlPrimitive.Int64)));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseByKeyCommand SpawnByKeyCommand(");
        emitted.RecordBody.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseByKeyCommand PeekByKeyCommand(");
        emitted.SiblingTypes.Should().Contain("public static class VaultExtensions");
        emitted.SiblingTypes.Should().Contain("public static class VaultNonContractExtensions");
        emitted.SiblingTypes.Should().NotContain("ByKeyCommand",
            "the key-addressed builders are reached through the template record, not through an extensions class");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_is_not_an_extension_method_on_the_key_type()
    {
        var template = Template(new DamlPrimitiveType(DamlPrimitive.Text), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("string key)");
        emitted.RecordBody.Should().NotContain("this string key",
            "extending string would put the method on every string in the consuming project");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_guards_a_reference_typed_key_against_null()
    {
        var template = Template(new DamlTypeRef(LocalPackageId, "Main", "VaultKey"), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("ArgumentNullException.ThrowIfNull(key);");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_leaves_a_value_typed_key_unguarded()
    {
        var template = Template(new DamlPrimitiveType(DamlPrimitive.Party), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("Party key)");
        emitted.RecordBody.Should().NotContain("ArgumentNullException.ThrowIfNull(key);",
            "Party is a struct, so the guard would only box the argument");
        emitted.RecordBody.Should().Contain("key.ToDamlValue(),");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_guards_a_nested_optional_key_against_null()
    {
        var template = Template(OptionalOf(OptionalOf(new DamlPrimitiveType(DamlPrimitive.Text))), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("global::Daml.Runtime.Stdlib.Optional<global::Daml.Runtime.Stdlib.Optional<string>> key)");
        emitted.RecordBody.Should().Contain("ArgumentNullException.ThrowIfNull(key);",
            "the wrapper is a non-nullable reference type, so without the guard an absent key "
            + "surfaces as a NullReferenceException inside ToValue rather than at the boundary");
    }

    [Fact]
    public void ChoiceEmitterByKeyCommand_leaves_a_flat_optional_key_unguarded()
    {
        var template = Template(OptionalOf(new DamlPrimitiveType(DamlPrimitive.Text)), Choice("Spawn", ContractIdOf("Token")));

        var emitted = Emit(template);

        emitted.RecordBody.Should().Contain("string? key)");
        emitted.RecordBody.Should().NotContain("ArgumentNullException.ThrowIfNull(key);",
            "a flat Optional stays on C# nullable syntax, so null is the absent key rather than a bug");
    }

    private static DamlTypeApp OptionalOf(DamlType argument) =>
        new(new DamlPrimitiveType(DamlPrimitive.Optional), [argument]);

    [Fact]
    public void ChoiceEmitterByKeyCommand_is_absent_from_a_key_less_template()
    {
        var template = Template(key: null, Choice("Spawn", ContractIdOf("Token")), Choice("Peek", new DamlPrimitiveType(DamlPrimitive.Int64)));

        var emitted = Emit(template);

        emitted.SiblingTypes.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand SpawnCommand(");
        emitted.SiblingTypes.Should().Contain("public static global::Daml.Runtime.Commands.ExerciseCommand PeekCommand(");
        emitted.RecordBody.Should().NotContain("ByKeyCommand");
        emitted.SiblingTypes.Should().NotContain("ByKeyCommand");
    }
}
