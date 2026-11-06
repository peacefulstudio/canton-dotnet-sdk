// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Tests.TestHelpers;
using Daml.Codegen.Intermediate.Model;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;
using static Daml.Codegen.CSharp.Tests.TestHelpers.DamlModelBuilder;
using static Daml.Codegen.CSharp.Tests.TestHelpers.GeneratorFactory;

namespace Daml.Codegen.CSharp.Tests;

public class EmittedLiteralConstantsTests
{
    private const string ConstantsPath = "Acme/Registry/Constants.cs";

    private static DamlModule ModuleOf(string name, params DamlConstant[] constants) =>
        new()
        {
            Name = name,
            Templates = [],
            DataTypes = [],
            Interfaces = [],
            Constants = constants,
        };

    private static DamlModule RegistryOf(params DamlConstant[] constants) => ModuleOf("Acme.Registry", constants);

    private static DamlConstant Text(string name, string value) => new(name, new DamlTextConstantValue(value));

    private static DamlConstant Int64(string name, long value) => new(name, new DamlInt64ConstantValue(value));

    private static DamlConstant Bool(string name, bool value) => new(name, new DamlBoolConstantValue(value));

    private static DamlConstant Numeric(string name, string value) => new(name, new DamlNumericConstantValue(value));

    private static string ConstantsSourceOf(DarModel dar, CodeGenOptions? options = null) =>
        CreateGenerator(options).Generate(dar).Single(file => file.RelativePath == ConstantsPath).Content;

    private static IReadOnlyList<GeneratedFile> GenerateRegistry(params DamlConstant[] constants) =>
        CreateGenerator().Generate(CreateTestDar(RegistryOf(constants)));

    [Fact]
    public void Generate_emits_a_Text_constant_as_a_public_const_string()
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(Text("greeting", "hello"))));

        source.Should().Contain("namespace Acme.Registry;")
            .And.Contain("public static class Constants")
            .And.Contain("public const string Greeting = \"hello\";");
    }

    [Fact]
    public void Generate_emits_an_Int64_constant_as_a_public_const_long()
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(
            Int64("retryLimit", 42),
            Int64("negativeLimit", -7),
            Int64("floor", long.MinValue),
            Int64("ceiling", long.MaxValue))));

        source.Should().Contain("public const long RetryLimit = 42;")
            .And.Contain("public const long NegativeLimit = -7;")
            .And.Contain("public const long Floor = -9223372036854775808;")
            .And.Contain("public const long Ceiling = 9223372036854775807;");
    }

    [Fact]
    public void Generate_emits_a_Bool_constant_as_a_public_const_bool()
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(
            Bool("maintenanceMode", true),
            Bool("disabled", false))));

        source.Should().Contain("public const bool MaintenanceMode = true;")
            .And.Contain("public const bool Disabled = false;");
    }

    [Theory]
    [InlineData("0.0025000000", "0.0025m")]
    [InlineData("-1.5000000000", "-1.5m")]
    [InlineData("3.0000000000", "3m")]
    [InlineData("0.0000000000", "0m")]
    [InlineData("-0.0000000000", "0m")]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001m")]
    [InlineData("0.0000000000000000000000000001000000000", "0.0000000000000000000000000001m")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335m")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335m")]
    [InlineData("7922816251426433759354395033.5", "7922816251426433759354395033.5m")]
    public void Generate_emits_a_Numeric_constant_that_decimal_holds_exactly_as_a_public_const_decimal(string numeric, string expectedLiteral)
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(Numeric("feeRate", numeric))));

        source.Should().Contain($"public const decimal FeeRate = {expectedLiteral};");
    }

    [Theory]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("0.0000000000000000000000000000000000001")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("-79228162514264337593543950336")]
    [InlineData("7922816251426433759354395033.6")]
    public void Generate_skips_a_Numeric_constant_that_decimal_cannot_hold_exactly(string numeric)
    {
        var files = GenerateRegistry(Numeric("tinyRate", numeric), Int64("kept", 1));

        files.Single(file => file.RelativePath == ConstantsPath).Content
            .Should().Contain("public const long Kept = 1;")
            .And.NotContain("TinyRate");
    }

    [Fact]
    public void Generate_logs_each_skipped_Numeric_constant_naming_the_module_value_and_literal()
    {
        var logger = new CapturingLogger();
        var generator = new CSharpCodeGenerator(new CodeGenOptions(), new ForwardingLogger(logger));

        generator.Generate(CreateTestDar(RegistryOf(Numeric("tinyRate", "0.0000000000000000000000000000000000001"))));

        var record = logger.Records.Should().ContainSingle(entry => entry.Message.Contains("tinyRate")).Subject;
        record.Level.Should().Be(LogLevel.Information);
        record.Message.Should().Contain("Acme.Registry").And.Contain("0.0000000000000000000000000000000000001").And.Contain("decimal");
    }

    [Fact]
    public void Generate_emits_no_Constants_file_for_a_module_without_constants()
    {
        var files = CreateGenerator().Generate(CreateTestDar(RegistryOf()));

        files.Should().NotContain(file => file.RelativePath == ConstantsPath);
    }

    [Fact]
    public void Generate_emits_no_Constants_file_when_every_constant_is_skipped()
    {
        var files = GenerateRegistry(Numeric("tinyRate", "0.0000000000000000000000000000000000001"));

        files.Should().NotContain(file => file.RelativePath == ConstantsPath);
    }

    [Fact]
    public void Generate_escapes_a_Text_constant_so_the_source_stays_ascii_and_the_value_survives_compilation()
    {
        const string value = "say \"hi\"\n\\ tab\t\u00E9 \U0001F389 sep\u2028 nul\0 end";

        var files = GenerateRegistry(Text("greeting", value));

        files.Single(file => file.RelativePath == ConstantsPath).Content
            .Should().Contain("public const string Greeting = \"say \\\"hi\\\"\\n\\\\ tab\\t\\u00E9 \\uD83C\\uDF89 sep\\u2028 nul\\u0000 end\";");
        EmitToAssembly(files).GetType("Acme.Registry.Constants")!.GetField("Greeting")!.GetRawConstantValue()
            .Should().Be(value);
    }

    [Fact]
    public void Generate_emits_a_Text_constant_that_looks_like_source_as_an_inert_literal()
    {
        const string value = "\"; System.Console.Clear(); //";

        var files = GenerateRegistry(Text("payload", value));

        EmitToAssembly(files).GetType("Acme.Registry.Constants")!.GetField("Payload")!.GetRawConstantValue()
            .Should().Be(value);
    }

    [Fact]
    public void Generate_emits_every_literal_kind_into_one_class_that_compiles_with_the_values_intact()
    {
        var files = GenerateRegistry(
            Text("greeting", "hello"),
            Int64("retryLimit", 42),
            Bool("maintenanceMode", true),
            Numeric("feeRate", "0.0025000000"),
            Numeric("negativeFee", "-1.5000000000"));

        var constants = EmitToAssembly(files).GetType("Acme.Registry.Constants")!;

        constants.IsAbstract.Should().BeTrue("a static class is abstract and sealed");
        constants.IsSealed.Should().BeTrue();
        constants.GetField("Greeting")!.GetRawConstantValue().Should().Be("hello");
        constants.GetField("RetryLimit")!.GetRawConstantValue().Should().Be(42L);
        constants.GetField("MaintenanceMode")!.GetRawConstantValue().Should().Be(true);
        constants.GetField("FeeRate")!.GetValue(null).Should().Be(0.0025m);
        constants.GetField("NegativeFee")!.GetValue(null).Should().Be(-1.5m);
    }

    [Fact]
    public void Generate_orders_constants_by_Daml_name_whatever_order_the_model_lists_them_in()
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(Int64("zeta", 1), Int64("alpha", 2), Int64("mid", 3))));

        source.IndexOf("Alpha =", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("Mid =", StringComparison.Ordinal));
        source.IndexOf("Mid =", StringComparison.Ordinal).Should().BeLessThan(source.IndexOf("Zeta =", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_documents_each_constant_with_its_Daml_name_when_xml_docs_are_on()
    {
        var source = ConstantsSourceOf(CreateTestDar(RegistryOf(Int64("retryLimit", 42))));

        source.Should().Contain("/// <summary>Daml value retryLimit of module Acme.Registry.</summary>");
    }

    [Fact]
    public void Generate_omits_the_doc_comments_when_xml_docs_are_off()
    {
        var source = ConstantsSourceOf(
            CreateTestDar(RegistryOf(Int64("retryLimit", 42))),
            new CodeGenOptions { GenerateXmlDocs = false });

        source.Should().NotContain("///").And.Contain("public const long RetryLimit = 42;");
    }

    [Fact]
    public void Generate_applies_the_namespace_prefix_to_the_Constants_class()
    {
        var files = CreateGenerator(new CodeGenOptions { NamespacePrefix = "Company.Bindings" })
            .Generate(CreateTestDar(RegistryOf(Int64("retryLimit", 42))));

        files.Single(file => file.RelativePath == "Company/Bindings/Acme/Registry/Constants.cs").Content
            .Should().Contain("namespace Company.Bindings.Acme.Registry;");
    }

    [Theory]
    [InlineData("retry_limit", "RetryLimit")]
    [InlineData("retryLimit", "RetryLimit")]
    [InlineData("lock", "Lock")]
    [InlineData("class", "Class")]
    [InlineData("equals", "Equals_")]
    [InlineData("getHashCode", "GetHashCode_")]
    [InlineData("toString", "ToString_")]
    [InlineData("getType", "GetType_")]
    [InlineData("referenceEquals", "ReferenceEquals_")]
    [InlineData("memberwiseClone", "MemberwiseClone_")]
    [InlineData("finalize", "Finalize_")]
    [InlineData("constants", "Constants_")]
    public void Generate_names_a_constant_in_PascalCase_clear_of_keywords_and_object_members(string damlName, string expectedMember)
    {
        var files = GenerateRegistry(Int64(damlName, 1));

        files.Single(file => file.RelativePath == ConstantsPath).Content
            .Should().Contain($"public const long {expectedMember} = 1;");
        CompileEmittedFilesWithDocDiagnostics(files)
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .Should().BeEmpty("a constant named like an inherited object member must not hide it");
    }

    [Fact]
    public void Generate_suffixes_the_later_constant_when_two_Daml_names_case_to_the_same_member()
    {
        var files = GenerateRegistry(Int64("retry_limit", 1), Int64("retryLimit", 2));

        var source = files.Single(file => file.RelativePath == ConstantsPath).Content;
        source.Should().Contain("public const long RetryLimit = 2;")
            .And.Contain("public const long RetryLimit_ = 1;");
        CompileEmittedFiles(files).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Generate_names_the_class_Constants_with_a_trailing_underscore_when_a_type_of_the_module_has_that_name()
    {
        var module = new DamlModule
        {
            Name = "Acme.Registry",
            Templates = [],
            DataTypes =
            [
                new DamlDataType
                {
                    Name = "Constants",
                    Definition = new DamlRecordDefinition([new DamlFieldDefinition("x", new DamlPrimitiveType(DamlPrimitive.Int64))]),
                },
            ],
            Interfaces = [],
            Constants = [Int64("retryLimit", 42)],
        };

        var files = CreateGenerator().Generate(CreateTestDar(module));

        files.Single(file => file.RelativePath == "Acme/Registry/Constants_.cs").Content
            .Should().Contain("public static class Constants_");
        CompileEmittedFiles(files).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Generate_names_the_class_clear_of_a_child_namespace_of_the_module()
    {
        var dar = new DamlModelBuilder()
            .WithModules(
                RegistryOf(Int64("retryLimit", 42)),
                ModuleOf("Acme.Registry.Constants", Int64("deep", 1)))
            .Build();

        var files = CreateGenerator().Generate(dar);

        files.Single(file => file.RelativePath == "Acme/Registry/Constants_.cs").Content
            .Should().Contain("public static class Constants_");
        files.Single(file => file.RelativePath == "Acme/Registry/Constants/Constants.cs").Content
            .Should().Contain("namespace Acme.Registry.Constants;").And.Contain("public const long Deep = 1;");
        CompileEmittedFiles(files).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Generate_emits_only_the_constants_the_root_filter_admits()
    {
        var generator = CreateGenerator(new CodeGenOptions { RootFilter = @"^Acme\.Registry:retry" });

        var files = generator.Generate(CreateTestDar(RegistryOf(Int64("retryLimit", 42), Int64("timeout", 5))));

        files.Single(file => file.RelativePath == ConstantsPath).Content
            .Should().Contain("RetryLimit").And.NotContain("Timeout");
    }

    [Fact]
    public void Generate_emits_no_Constants_file_when_the_root_filter_admits_none_of_them()
    {
        var generator = CreateGenerator(new CodeGenOptions { RootFilter = @"^Elsewhere:" });

        var files = generator.Generate(CreateTestDar(RegistryOf(Int64("retryLimit", 42))));

        files.Should().NotContain(file => file.RelativePath == ConstantsPath);
    }

    [Fact]
    public void Generate_emits_the_constants_of_an_included_dependency_package()
    {
        var dependency = new DamlModelBuilder()
            .WithPackageId("dependency-package-id")
            .WithPackageName("dependency-package")
            .WithModule(ModuleOf("Lib.Limits", Int64("maxRetries", 9)))
            .BuildPackage();
        var dar = new DamlModelBuilder()
            .WithModule(RegistryOf(Int64("retryLimit", 42)))
            .WithDependency(dependency)
            .Build();

        var files = CreateGenerator(new CodeGenOptions { IncludeDependencies = true }).Generate(dar);

        files.Single(file => file.RelativePath == "Lib/Limits/Constants.cs").Content
            .Should().Contain("public const long MaxRetries = 9;");
    }

    [Fact]
    public void Generate_emits_the_same_constants_source_on_every_run()
    {
        var dar = CreateTestDar(RegistryOf(Text("greeting", "hello"), Numeric("feeRate", "0.0025000000"), Int64("retryLimit", 42)));

        ConstantsSourceOf(dar).Should().Be(ConstantsSourceOf(dar));
    }

    private sealed class ForwardingLogger(ILogger inner) : ILogger<CSharpCodeGenerator>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
