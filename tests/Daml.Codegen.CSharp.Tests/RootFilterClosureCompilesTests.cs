// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.Intermediate;
using Daml.Codegen.Intermediate.Model;
using AwesomeAssertions;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public sealed class RootFilterClosureCompilesTests
{
    [Theory]
    [InlineData(@"^RichTypes:Asset$")]
    [InlineData(@"^RichTypes:Holding$")]
    [InlineData(@"^RichTypes:Marker$")]
    [InlineData(@"^SubmitShapes:Ticket$")]
    [InlineData(@"^NoSuchModule:")]
    public async Task Generate_root_filtered_richtypes_output_compiles(string rootFilter)
    {
        var files = new CSharpCodeGenerator(new CodeGenOptions { RootFilter = rootFilter })
            .Generate(await ReadRichTypesSnapshot());

        var errors = EmittedCodeCompilesTestHelpers.CompileEmittedFiles(files)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage(CultureInfo.InvariantCulture)} @ {diagnostic.Location.GetLineSpan()}");

        errors.Should().BeEmpty("a --root filter must emit a closure that compiles on its own");
    }

    [Fact]
    public async Task Generate_root_filter_on_a_template_adds_the_interfaces_it_implements()
    {
        var files = new CSharpCodeGenerator(new CodeGenOptions { RootFilter = @"^RichTypes:Asset$" })
            .Generate(await ReadRichTypesSnapshot());

        files.Select(file => file.RelativePath).Should().Contain(["RichTypes/Asset.cs", "RichTypes/IHolding.cs", "RichTypes/IAnnotated.cs"]);
    }

    [Fact]
    public void Generate_root_filter_adds_a_template_an_emitted_record_holds_a_contract_id_of()
    {
        var logger = new CapturingLogger();
        var files = new CSharpCodeGenerator(new CodeGenOptions { RootFilter = @"^Test\.Module:Wanted$" }, new ForwardingLogger(logger))
            .Generate(ContractIdHolderDar());

        files.Select(file => file.RelativePath).Should().Contain(["Test/Module/Wanted.cs", "Test/Module/Held.cs", "Test/Module/Holder.cs"]);
        EmittedCodeCompilesTestHelpers.CompileEmittedFiles(files)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        logger.Records.Should().ContainSingle(record => record.Level == LogLevel.Information
            && record.Message == "Emitting Test.Module:Held, which the root filter does not match, because it is a field type of Test.Module:Holder: without it the emitted code would not compile");
    }

    [Fact]
    public void Generate_without_a_root_filter_logs_no_admitted_type()
    {
        var logger = new CapturingLogger();
        new CSharpCodeGenerator(new CodeGenOptions(), new ForwardingLogger(logger)).Generate(ContractIdHolderDar());

        logger.Records.Should().NotContain(record => record.Message.StartsWith("Emitting ", StringComparison.Ordinal));
    }

    private static DarModel ContractIdHolderDar()
    {
        var owner = new DamlFieldDefinition("owner", new DamlPrimitiveType(DamlPrimitive.Party));
        var module = new DamlModule
        {
            Name = "Test.Module",
            Templates =
            [
                new DamlTemplate { Name = "Wanted", Choices = [] },
                new DamlTemplate { Name = "Held", Choices = [] },
            ],
            DataTypes =
            [
                new DamlDataType { Name = "Wanted", Definition = new DamlRecordDefinition([owner]) },
                new DamlDataType { Name = "Held", Definition = new DamlRecordDefinition([owner]) },
                new DamlDataType
                {
                    Name = "Holder",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("held", EmittedCodeCompilesTestHelpers.ContractIdOf("Held"))]),
                },
            ],
            Interfaces = [],
        };
        var package = new DamlPackage
        {
            PackageId = "test-package-id",
            Name = "test-package",
            Version = new Version(1, 0, 0),
            LfVersion = "2.1",
            Modules = [module],
            DependencyReferences = [],
        };
        return new DarModel { MainPackage = package, Dependencies = [] };
    }

    private sealed class ForwardingLogger(ILogger inner) : ILogger<CSharpCodeGenerator>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    private static async Task<DarModel> ReadRichTypesSnapshot()
    {
        var protoPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", "richtypes", "intermediate.binpb");
        await using var stream = File.OpenRead(protoPath);
        return IntermediateDarReader.Read(IntermediateDar.Parser.ParseFrom(stream));
    }
}
