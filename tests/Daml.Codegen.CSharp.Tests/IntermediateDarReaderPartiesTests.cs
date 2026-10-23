// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;
using PbBuiltinType = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbDynamicParties = Daml.Codegen.Intermediate.DynamicParties;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbPartyAnalysis = Daml.Codegen.Intermediate.PartyAnalysis;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbStaticParties = Daml.Codegen.Intermediate.StaticParties;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.CSharp.Tests;

public partial class IntermediateDarReaderTests
{
    [Fact]
    public void IntermediateDarReader_template_signatories_default_to_dynamic()
    {
        var proto = MakePackageWith(module =>
        {
            module.DataTypes.Add(new PbDataType
            {
                Name = "T",
                Record = new PbRecord { Fields = { PartyField("party") } },
            });
            module.Templates.Add(new PbTemplate { Name = "T" });
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates.Single();
        template.Signatories.Source.Should().Be(DamlPartySource.Dynamic);
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void IntermediateDarReader_template_with_unset_signatories_defaults_to_dynamic()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
        });

        var model = IntermediateDarReader.Read(proto);

        var template = model.MainPackage.Modules[0].Templates[0];
        template.Signatories.Source.Should().Be(DamlPartySource.Dynamic);
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void IntermediateDarReader_template_with_static_signatories_reads_payload_field_references()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
            Signatories = new PbPartyAnalysis
            {
                Static = new PbStaticParties { PayloadFields = { "platform", "initiator" } },
            },
            Observers = new PbPartyAnalysis
            {
                Dynamic = new PbDynamicParties(),
            },
        });

        var model = IntermediateDarReader.Read(proto);
        var template = model.MainPackage.Modules[0].Templates[0];

        template.Signatories.Source.Should().Be(DamlPartySource.Static);
        template.Signatories.Parties.Should().HaveCount(2);
        template.Signatories.Parties.Select(p => ((DamlPartyPayloadField)p).FieldName)
            .Should().Equal("platform", "initiator");
        template.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    [Fact]
    public void IntermediateDarReader_choice_party_analysis_round_trips_static_and_dynamic()
    {
        var proto = MakeProtoWithSingleTemplate(new PbTemplate
        {
            Name = "Tpl",
            Choices =
            {
                new PbChoice
                {
                    Name = "Archive",
                    Consuming = true,
                    ArgumentType = BuiltinType(PbBuiltinType.Unit),
                    ReturnType = BuiltinType(PbBuiltinType.Unit),
                    Controllers = new PbPartyAnalysis
                    {
                        Static = new PbStaticParties { PayloadFields = { "owner" } },
                    },
                    Observers = new PbPartyAnalysis { Dynamic = new PbDynamicParties() },
                },
            },
        });

        var model = IntermediateDarReader.Read(proto);
        var choice = model.MainPackage.Modules[0].Templates[0].Choices[0];

        choice.Controllers.Source.Should().Be(DamlPartySource.Static);
        choice.Controllers.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("owner");
        choice.Observers.Source.Should().Be(DamlPartySource.Dynamic);
    }

    private static IntermediateDar MakeProtoWithSingleTemplate(PbTemplate template) =>
        new()
        {
            Main = new PbPackage
            {
                PackageId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                PackageName = "test",
                PackageVersion = "1.0.0",
                LanguageVersion = "2.1",
                Modules =
                {
                    new PbModule { NameSegments = { "M" }, Templates = { template } },
                },
            },
        };

    private static PbType BuiltinType(PbBuiltinType b) => new() { Builtin = b };
}
