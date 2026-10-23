// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Codegen.CSharp.Cli;
using Daml.Codegen.Intermediate;
using Google.Protobuf;
using Xunit;
using PbRecord = Daml.Codegen.Intermediate.Record;
using PbType = Daml.Codegen.Intermediate.Type;

namespace Daml.Codegen.CSharp.Cli.Tests;

[Collection("ConsoleRedirection")]
public class CliIntermediateInputTests : IDisposable
{
    private readonly string _workspace;

    public CliIntermediateInputTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"cli-input-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CliIntermediateInput_generates_a_record_whose_field_nests_lists_sixty_levels_deep()
    {
        var intermediate = WriteIntermediateWithNestedListField(levels: 60);

        var (exit, stderr) = await Run(intermediate);

        exit.Should().Be(0, stderr);
    }

    [Fact]
    public async Task CliIntermediateInput_names_the_nesting_depth_when_a_field_type_exceeds_the_supported_levels()
    {
        var intermediate = WriteIntermediateWithNestedListField(levels: 700);

        var (exit, stderr) = await Run(intermediate);

        exit.Should().Be(1);
        stderr.Should().Contain("capped at 1024 protobuf levels");
        stderr.Should().Contain("nested.binpb");
    }

    [Fact]
    public async Task CliIntermediateInput_points_a_dar_handed_as_the_intermediate_file_at_dar2intermediate()
    {
        var dar = Path.Combine(_workspace, "package.dar");
        await File.WriteAllBytesAsync(dar, [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00], TestContext.Current.CancellationToken);

        var (exit, stderr) = await Run(dar);

        exit.Should().Be(1);
        stderr.Should().Contain("package.dar is a DAR archive, not an intermediate DAR");
        stderr.Should().Contain("dar2intermediate");
        stderr.Should().Contain("dpm codegen-cs");
        stderr.Should().NotContain("invalid tag");
    }

    private string WriteIntermediateWithNestedListField(int levels)
    {
        PbType fieldType = new() { Builtin = BuiltinType.Int64 };
        for (var level = 0; level < levels; level++)
        {
            var list = new TypeApp { Function = new PbType { Builtin = BuiltinType.List } };
            list.Arguments.Add(fieldType);
            fieldType = new PbType { TypeApp = list };
        }

        var record = new PbRecord();
        record.Fields.Add(new Field { Name = "deep", Type = fieldType });
        var dataType = new DataType { Name = "Deep", Record = record };
        var module = new IntermediateModule();
        module.NameSegments.Add("Test");
        module.DataTypes.Add(dataType);
        var package = new IntermediatePackage
        {
            PackageId = "4444444444444444444444444444444444444444444444444444444444444444",
            PackageName = "nested",
            PackageVersion = "1.0.0",
            LanguageVersion = "2.1",
        };
        package.Modules.Add(module);

        var path = Path.Combine(_workspace, "nested.binpb");
        File.WriteAllBytes(path, new IntermediateDar { Main = package }.ToByteArray());
        return path;
    }

    private async Task<(int Exit, string StdErr)> Run(string intermediate)
    {
        var originalError = Console.Error;
        using var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            var exit = await Program.Main(["--intermediate", intermediate, "-o", Path.Combine(_workspace, "out")]);
            return (exit, capture.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
