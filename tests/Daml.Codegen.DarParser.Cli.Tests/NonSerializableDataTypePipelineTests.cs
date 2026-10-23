// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.DarParser.Cli;
using Daml.Codegen.Intermediate;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Cli.Tests;

/// <summary>
/// Drives the two-step internal pipeline — <c>dar2intermediate</c> through <see cref="Program.Main"/>,
/// then the <c>--intermediate</c> read and emit — over the <c>conformance/nonserializable</c> corpus,
/// whose <c>data Fn = Fn with f : Int -> Int</c> is a non-serializable data type the JVM helper strips.
/// </summary>
public sealed class NonSerializableDataTypePipelineTests : IDisposable
{
    private readonly string _workspace;

    public NonSerializableDataTypePipelineTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"nonserializable-pipeline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task NonSerializableDataTypePipeline_emits_the_template_and_no_type_for_the_function_field_record()
    {
        var generated = await RunTwoStepPipelineAsync();

        generated.Should().Contain(f => f.Content.Contains("class Ledger"),
            "the corpus template must still be generated once its non-serializable sibling is stripped");
        generated.Should().NotContain(f => Path.GetFileName(f.RelativePath) == "Fn.cs");
        generated.Should().NotContain(f => Regex.IsMatch(f.Content, @"\b(class|record|struct|interface)\s+Fn\b"),
            "the JVM helper strips non-serializable `Fn`, so the .NET pipeline must not emit a type for it either");
    }

    [Fact]
    public async Task NonSerializableDataTypePipeline_intermediate_carries_no_data_type_named_fn()
    {
        var intermediate = await RunDar2IntermediateAsync();

        intermediate.Main.Modules.Should().ContainSingle(m => string.Join(".", m.NameSegments) == "NonSerializable")
            .Which.DataTypes.Select(dt => dt.Name).Should().Equal("Increment", "Ledger");
    }

    private async Task<IntermediateDar> RunDar2IntermediateAsync()
    {
        var dar = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nonserializable.dar");
        File.Exists(dar).Should().BeTrue($"the conformance/nonserializable corpus DAR must ship at {dar}");
        var output = Path.Combine(_workspace, "intermediate.binpb");

        var exit = await Program.Main(["dar2intermediate", dar, "-o", output]);

        exit.Should().Be(0);
        var bytes = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
        return IntermediateDar.Parser.ParseFrom(bytes);
    }

    private async Task<IReadOnlyList<GeneratedFile>> RunTwoStepPipelineAsync()
    {
        var model = IntermediateDarReader.Read(await RunDar2IntermediateAsync());
        return new CSharpCodeGenerator(new CodeGenOptions()).Generate(model);
    }
}
