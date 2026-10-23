// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

const int UnrecognizedInvocation = 64;
const string DefaultVersion = "1.0.22";
const string MarkerSource = "namespace FakeDpmGenerated { public sealed class Marker { } }\n";

if (args.Length > 0 && string.Equals(args[0], "--version", StringComparison.Ordinal))
{
    var banner = Environment.GetEnvironmentVariable("FAKE_DPM_BANNER");
    if (!string.IsNullOrEmpty(banner))
    {
        Console.WriteLine(banner);
    }
    var version = Environment.GetEnvironmentVariable("FAKE_DPM_VERSION");
    Console.WriteLine($"version: {(string.IsNullOrEmpty(version) ? DefaultVersion : version)}");
    Console.WriteLine("build: b65a815");
    Console.WriteLine("buildDate: b65a815");
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "version", StringComparison.Ordinal))
{
    Console.WriteLine(" * 3.6.0 ");
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "build", StringComparison.Ordinal))
{
    string? outputPath = null;
    for (var index = 1; index + 1 < args.Length; index++)
    {
        if (string.Equals(args[index], "-o", StringComparison.Ordinal))
        {
            outputPath = args[index + 1];
            break;
        }
    }

    if (!File.Exists("daml.yaml"))
    {
        Console.Error.WriteLine($"fake-dpm: no daml.yaml found in {Directory.GetCurrentDirectory()}");
        return 5;
    }
    if (string.IsNullOrEmpty(outputPath))
    {
        Console.Error.WriteLine("fake-dpm: -o is required");
        return 2;
    }

    var buildLog = Environment.GetEnvironmentVariable("FAKE_DPM_BUILD_LOG");
    if (!string.IsNullOrEmpty(buildLog))
    {
        File.AppendAllText(buildLog, "build" + Environment.NewLine);
    }

    var failureCode = Environment.GetEnvironmentVariable("FAKE_DPM_BUILD_EXIT");
    if (!string.IsNullOrEmpty(failureCode) && failureCode != "0")
    {
        Console.Error.WriteLine("fake-dpm: build failed");
        return int.Parse(failureCode, System.Globalization.CultureInfo.InvariantCulture);
    }

    var sourceDar = Environment.GetEnvironmentVariable("FAKE_DPM_BUILD_DAR");
    if (string.IsNullOrEmpty(sourceDar) || !File.Exists(sourceDar))
    {
        Console.Error.WriteLine($"fake-dpm: FAKE_DPM_BUILD_DAR not found: {sourceDar}");
        return 1;
    }

    File.WriteAllBytes(outputPath, File.ReadAllBytes(sourceDar));
    return 0;
}

if (args.Length > 0 && string.Equals(args[0], "codegen-cs", StringComparison.Ordinal))
{
    string? dar = null;
    string? outputDirectory = null;
    var index = 1;
    while (index < args.Length)
    {
        if (string.Equals(args[index], "--", StringComparison.Ordinal))
        {
            break;
        }
        if (string.Equals(args[index], "--dar", StringComparison.Ordinal) && index + 1 < args.Length)
        {
            dar = args[index + 1];
            index += 2;
            continue;
        }
        if (string.Equals(args[index], "--out", StringComparison.Ordinal) && index + 1 < args.Length)
        {
            outputDirectory = args[index + 1];
            index += 2;
            continue;
        }
        index++;
    }

    if (!File.Exists("daml.yaml"))
    {
        Console.Error.WriteLine($"fake-dpm: no daml.yaml component pin found in {Directory.GetCurrentDirectory()}");
        return 5;
    }
    if (!DigestPinnedComponent().IsMatch(File.ReadAllText("daml.yaml")))
    {
        Console.Error.WriteLine("fake-dpm: the daml.yaml component pin carries no @sha256: digest");
        return 6;
    }
    if (!string.Equals(Environment.GetEnvironmentVariable("DPM_AUTO_INSTALL"), "true", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("fake-dpm: the pinned component is currently not installed. Run `dpm install package` to install");
        return 7;
    }
    var codegenLog = Environment.GetEnvironmentVariable("FAKE_DPM_CODEGEN_LOG");
    if (!string.IsNullOrEmpty(codegenLog))
    {
        File.AppendAllText(codegenLog, $"DPM_HOME={Environment.GetEnvironmentVariable("DPM_HOME")}{Environment.NewLine}");
    }
    if (string.IsNullOrEmpty(dar) || !File.Exists(dar))
    {
        Console.Error.WriteLine($"fake-dpm: DAR not found: {dar}");
        return 1;
    }
    if (string.IsNullOrEmpty(outputDirectory))
    {
        Console.Error.WriteLine("fake-dpm: --out is required");
        return 2;
    }

    Directory.CreateDirectory(outputDirectory);
    File.WriteAllText(Path.Combine(outputDirectory, "Marker.cs"), MarkerSource);
    return 0;
}

Console.Error.WriteLine($"fake-dpm: unrecognized invocation: {string.Join(' ', args)}");
return UnrecognizedInvocation;

internal partial class Program
{
    [System.Text.RegularExpressions.GeneratedRegex(@"oci://[^""\s]+@sha256:[0-9a-f]{64}")]
    private static partial System.Text.RegularExpressions.Regex DigestPinnedComponent();
}
