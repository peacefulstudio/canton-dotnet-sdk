// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Xunit;
using static Daml.Codegen.CSharp.Tests.TestHelpers.EmittedSubmissionShape;

namespace Daml.Codegen.CSharp.Tests;

public class SubmissionExtensionsEmitterTests
{
    private sealed record UnknownPartyReference : DamlPartyReference;

    private readonly PartyAnalysis _party = new();

    private static DamlFieldDefinition PartyField(string name) =>
        new(name, new DamlPrimitiveType(DamlPrimitive.Party));

    private static DamlPartyAnalysis StaticParties(params string[] fieldNames) =>
        DamlPartyAnalysis.Static(fieldNames.Select(n => (DamlPartyReference)new DamlPartyPayloadField(n)).ToList());

    private sealed record TemplateFixture(DamlTemplate Template, IReadOnlyList<DamlFieldDefinition> Fields);

    private static TemplateFixture Template(
        string name,
        IReadOnlyList<DamlFieldDefinition> fields,
        DamlPartyAnalysis? signatories = null,
        DamlPartyAnalysis? observers = null) =>
        new(
            new DamlTemplate
            {
                Name = name,
                Choices = [],
                Signatories = signatories ?? DamlPartyAnalysis.Dynamic,
                Observers = observers ?? DamlPartyAnalysis.Dynamic,
            },
            fields);

    private static PackageEmitContext Context(DamlTemplate template)
    {
        var package = new DamlPackage
        {
            PackageId = "pkg",
            Name = "Acme",
            Version = new Version(1, 0, 0),
            LfVersion = "1.15",
            Modules =
            [
                new DamlModule
                {
                    Name = "Main",
                    Templates = [template],
                    DataTypes = [],
                    Interfaces = [],
                },
            ],
            DependencyReferences = [],
        };
        return PackageEmitContext.ForPackage(package, new CodeGenOptions(), isMainPackage: true).Single();
    }

    private string Emit(TemplateFixture fixture, CodeGenOptions? options = null)
    {
        var context = Context(fixture.Template);
        var emitter = new SubmissionExtensionsEmitter(context, options ?? new CodeGenOptions(), _party);
        var sb = new StringBuilder();
        var indent = new IndentWriter(sb);
        emitter.TryWriteSubmissionExtensions(indent, fixture.Template, fixture.Fields);
        return sb.ToString();
    }

    [Fact]
    public void SubmissionExtensionsEmitter_emits_the_submission_extensions_class_for_a_template()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().Contain("public static class AssetSubmissionExtensions");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_emits_the_submission_extensions_class_even_when_signatories_are_dynamic()
    {
        var template = Template(
            "Vault",
            [PartyField("custodian")],
            signatories: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().Contain("public static class VaultSubmissionExtensions");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_static_single_signatory_create_derives_submitter_from_the_payload_and_omits_the_parameter()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter = payload.Owner;");
        output.Should().NotContain("SubmitterInfo submitter,");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_static_multiple_signatory_create_builds_a_submitter_set_from_the_payload_fields()
    {
        var template = Template(
            "Trade",
            [PartyField("buyer"), PartyField("seller")],
            signatories: StaticParties("buyer", "seller"));

        var output = Emit(template);

        output.Should().Contain("var submitter = new SubmitterInfo(new HashSet<Party>");
        output.Should().Contain("payload.Buyer,");
        output.Should().Contain("payload.Seller");
        output.Should().NotContain("SubmitterInfo submitter,");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_dynamic_signatory_create_takes_an_explicit_submitter_parameter()
    {
        var template = Template(
            "Keyed",
            [PartyField("custodian")],
            signatories: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().Contain("return global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TryCreateAsync<Keyed>(client, payload, submitter, configure: configure, cancellationToken: cancellationToken);");
        output.Should().NotContain("= payload.");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_static_non_empty_observers_emit_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author"), PartyField("reader")],
            signatories: StaticParties("author"),
            observers: StaticParties("reader"));

        var output = Emit(template);

        output.Should().Contain("public static IReadOnlyList<Party> Observers(Note payload)");
        output.Should().Contain("payload.Reader");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_static_empty_observers_skip_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author")],
            signatories: StaticParties("author"),
            observers: DamlPartyAnalysis.Static([]));

        var output = Emit(template);

        output.Should().NotContain("Observers(Note payload)");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_dynamic_observers_skip_the_observers_helper()
    {
        var template = Template(
            "Note",
            [PartyField("author")],
            signatories: StaticParties("author"),
            observers: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().NotContain("Observers(Note payload)");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_unknown_signatory_subtype_falls_back_to_an_explicit_submitter_parameter()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: DamlPartyAnalysis.Static([new UnknownPartyReference()]));

        var output = Emit(template);

        output.Should().Contain("SubmitterInfo submitter,");
        output.Should().NotContain("= payload.");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_static_create_declares_configure_before_the_cancellation_token_and_forwards_it()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().MatchRegex(@"Asset payload,\s*" + Regex.Escape(ConfigureParameter) + @"\s*CancellationToken cancellationToken = default\)");
        output.Should().Contain("return global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TryCreateAsync<Asset>(client, payload, submitter, configure: configure, cancellationToken: cancellationToken);");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_dynamic_create_declares_configure_after_the_submitter_and_forwards_it()
    {
        var template = Template(
            "Keyed",
            [PartyField("custodian")],
            signatories: DamlPartyAnalysis.Dynamic);

        var output = Emit(template);

        output.Should().MatchRegex(@"SubmitterInfo submitter,\s*" + Regex.Escape(ConfigureParameter) + @"\s*CancellationToken cancellationToken = default\)");
        output.Should().Contain("return global::Daml.Ledger.Abstractions.Extensions.SingleCommandExtensions.TryCreateAsync<Keyed>(client, payload, submitter, configure: configure, cancellationToken: cancellationToken);");
    }

    [Fact]
    public void SubmissionExtensionsEmitter_create_documents_configure_with_a_disclosure_example()
    {
        var template = Template(
            "Asset",
            [PartyField("owner")],
            signatories: StaticParties("owner"));

        var output = Emit(template);

        output.Should().Contain("/// <param name=\"configure\">");
        output.Should().Contain("s => s.WithDisclosedContracts(holding.Disclosure!)");
    }
}
