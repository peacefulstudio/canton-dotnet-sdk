// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Daml.Codegen.CSharp;
using Daml.Codegen.CSharp.CodeGen;
using Daml.Codegen.CSharp.Versioning;
using Daml.Codegen.Intermediate;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.CSharp.Cli;

internal static partial class Program
{
    internal static async Task<int> Main(string[] args)
    {
        var rootCommand = BuildRootCommand();
        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Declares every CLI option (with its validators and default) and wires the parsed
    /// values into <see cref="RunCodegen"/>. Split out of <see cref="Main"/> so the option
    /// surface — the part that grows as flags are added — is its own unit, separate from
    /// argument parsing and invocation.
    /// </summary>
    private static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Generate C# code from an IntermediateDar proto");

        var intermediateOption = new Option<FileInfo>("--intermediate")
        {
            Description = "Path to an IntermediateDar proto file produced by the JVM helper.",
            Required = true
        };
        intermediateOption.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<FileInfo?>();
            if (value is not null && !value.Exists)
            {
                result.AddError($"IntermediateDar proto not found: {value.FullName}");
            }
        });

        var outputOption = new Option<DirectoryInfo>("-o")
        {
            Description = "Output directory for generated sources",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
        };
        outputOption.Aliases.Add("--output-directory");

        var namespaceOption = new Option<string?>("-n")
        {
            Description = "Namespace prefix for the main package's generated code: each Daml module is emitted under <prefix>.<Module>, the prefix is elided when the module name already starts with it, and dependency packages are never prefixed (default: no prefix, the namespace is the module name)"
        };
        namespaceOption.Aliases.Add("--namespace");

        var verbosityOption = new Option<int>("-v")
        {
            Description = "Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug",
            DefaultValueFactory = _ => 1
        };
        verbosityOption.Aliases.Add("--verbosity");

        var rootOption = new Option<string?>("-r")
        {
            Description = "Regular expression to filter which templates to generate (default: .*)"
        };
        rootOption.Aliases.Add("--root");

        var nullableOption = new Option<bool>("--nullable")
        {
            Description = "Enable nullable reference types in generated code",
            DefaultValueFactory = _ => true
        };

        var generateProjectOption = new Option<bool>("--generate-project")
        {
            Description = "Generate a .csproj file for the generated code",
            DefaultValueFactory = _ => false
        };

        var includeDepsOption = new Option<bool>("--include-dependencies")
        {
            Description = "Generate code for dependency packages as well",
            DefaultValueFactory = _ => false
        };

        var targetFrameworkOption = new Option<string>("--target-framework")
        {
            Description = "Target framework for the generated project (e.g., net10.0)",
            DefaultValueFactory = _ => "net10.0"
        };
        targetFrameworkOption.Validators.Add(result =>
        {
            var value = result.GetValue(targetFrameworkOption);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--target-framework must be a non-empty target framework moniker (e.g. net10.0, net9.0).");
            }
        });

        var runtimeVersionOption = new Option<string?>("--runtime-version")
        {
            Description = "Version of Daml.Runtime package to reference"
        };
        runtimeVersionOption.Validators.Add(result =>
        {
            var value = result.GetValue(runtimeVersionOption);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--runtime-version must be a non-empty version string when specified (e.g. 1.2.3).");
            }
        });

        var emitterCounterOption = new Option<int>("--emitter-counter")
        {
            Description = "4th segment of the generated NuGet version (Major.Minor.Patch.Generation). Defaults to 0; set a monotonic counter to distinguish republished builds of the same source. Overridden by --release-counters, which resolves the segment as a codegen-generation ordinal.",
            DefaultValueFactory = _ => 0
        };
        emitterCounterOption.Validators.Add(result =>
        {
            if (result.GetValue(emitterCounterOption) < 0)
            {
                result.AddError("--emitter-counter must be a non-negative integer (the 4th version segment is a monotonic counter).");
            }
        });

        var releaseCountersOption = new Option<FileInfo?>("--release-counters")
        {
            Description = "Path to a JSON release-counter store. When set, the 4th NuGet version segment is resolved from this store as a codegen-generation ordinal keyed by --codegen-version, overriding --emitter-counter. The store is created on first use and atomically updated when a new codegen version is first seen."
        };

        var codegenVersionOption = new Option<string?>("--codegen-version")
        {
            Description = "Codegen-tool version that keys the release-counter generation ordinal (the 4th NuGet version segment). Every package produced by one codegen version shares the ordinal, which increments when the version changes. Defaults to this emitter build's informational version (AssemblyInformationalVersionAttribute) with any '+' build metadata stripped."
        };
        codegenVersionOption.Validators.Add(result =>
        {
            var value = result.GetValue(codegenVersionOption);
            if (value is null)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--codegen-version must be a non-empty version string when specified (e.g. 0.2.0-preview.3).");
                return;
            }
            if (result.GetValue(releaseCountersOption) is null)
            {
                result.AddError("--codegen-version has no effect without --release-counters; supply --release-counters <path> to key the generation ordinal, or drop --codegen-version.");
            }
        });

        var versionSuffixOption = new Option<string?>("--version-suffix")
        {
            Description = "SemVer prerelease suffix appended to generated package versions, e.g. 'preview.2'. Mirrors the emitter prerelease tag. No leading dash."
        };
        versionSuffixOption.Validators.Add(result =>
        {
            var value = result.GetValue(versionSuffixOption);
            if (value is not null && !FourPartPackageVersion.IsValidPrereleaseSuffix(value))
            {
                result.AddError($"--version-suffix '{value}' is not a valid SemVer prerelease suffix: it must be a non-empty dot-separated sequence of [0-9A-Za-z-] identifiers (e.g. preview.2), with no leading dash.");
            }
        });

        var packageLicenseOption = new Option<string>("--package-license")
        {
            Description = "SPDX license expression emitted in the generated .csproj's <PackageLicenseExpression>. Defaults to Apache-2.0.",
            DefaultValueFactory = _ => "Apache-2.0"
        };
        packageLicenseOption.Validators.Add(result =>
        {
            var value = result.GetValue(packageLicenseOption);
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--package-license must be a non-empty SPDX license expression (e.g. Apache-2.0, MIT, BSD-3-Clause).");
            }
        });

        var repositoryUrlOption = new Option<string?>("--repository-url")
        {
            Description = "Repository URL emitted in the generated .csproj's <PackageProjectUrl>/<RepositoryUrl>/<RepositoryType>. When omitted, those elements are not emitted."
        };
        repositoryUrlOption.Validators.Add(result =>
        {
            var value = result.GetValue(repositoryUrlOption);
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                result.AddError("--repository-url must be a non-empty URL when specified (e.g. https://github.com/acme/widgets).");
            }
        });

        rootCommand.Options.Add(intermediateOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(namespaceOption);
        rootCommand.Options.Add(verbosityOption);
        rootCommand.Options.Add(rootOption);
        rootCommand.Options.Add(nullableOption);
        rootCommand.Options.Add(generateProjectOption);
        rootCommand.Options.Add(includeDepsOption);
        rootCommand.Options.Add(targetFrameworkOption);
        rootCommand.Options.Add(runtimeVersionOption);
        rootCommand.Options.Add(emitterCounterOption);
        rootCommand.Options.Add(releaseCountersOption);
        rootCommand.Options.Add(codegenVersionOption);
        rootCommand.Options.Add(packageLicenseOption);
        rootCommand.Options.Add(versionSuffixOption);
        rootCommand.Options.Add(repositoryUrlOption);

        Func<ParseResult, CancellationToken, Task<int>> action = (parseResult, cancellationToken) =>
            RunCodegen(
                new CodegenArgs(
                    parseResult.GetValue(intermediateOption)!,
                    parseResult.GetValue(outputOption)!,
                    parseResult.GetValue(namespaceOption),
                    parseResult.GetValue(verbosityOption),
                    parseResult.GetValue(rootOption),
                    parseResult.GetValue(nullableOption),
                    parseResult.GetValue(generateProjectOption),
                    parseResult.GetValue(includeDepsOption),
                    parseResult.GetValue(targetFrameworkOption)!,
                    parseResult.GetValue(runtimeVersionOption),
                    parseResult.GetValue(emitterCounterOption),
                    parseResult.GetValue(releaseCountersOption),
                    parseResult.GetValue(codegenVersionOption),
                    parseResult.GetValue(packageLicenseOption)!,
                    parseResult.GetValue(versionSuffixOption),
                    parseResult.GetValue(repositoryUrlOption)),
                cancellationToken);
        rootCommand.SetAction(action);

        return rootCommand;
    }

    internal static async Task<int> RunCodegen(CodegenArgs args, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new VerbosityConsoleLoggerProvider(args.Verbosity))
                   .SetMinimumLevel(LogLevel.Trace));
        var logger = loggerFactory.CreateLogger<CSharpCodeGenerator>();

        try
        {
            var emitterVersion = typeof(Program).Assembly.GetName().Version;
            LogBanner(logger, emitterVersion);
            LogOutputDirectory(logger, args.OutputDirectory.FullName);

            if (!args.OutputDirectory.Exists)
            {
                args.OutputDirectory.Create();
                LogCreatedOutputDirectory(logger, args.OutputDirectory.FullName);
            }

            await GenerateFromIntermediate(args.IntermediateFile, args, logger, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCanceled(logger, args.OutputDirectory.FullName);
            return 130;
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex.Message);
            var rootCauseMessage = ex.GetBaseException().Message;
            if (rootCauseMessage != ex.Message)
            {
                LogRootCause(logger, rootCauseMessage);
            }
            if (args.Verbosity >= 3)
            {
                LogFailureDetail(logger, ex.ToString());
            }
            return 1;
        }
    }

    private static async Task GenerateFromIntermediate(FileInfo file, CodegenArgs args, ILogger<CSharpCodeGenerator> logger, CancellationToken cancellationToken)
    {
        LogReadingIntermediate(logger, file.Name);
        IntermediateDar proto;
        var stream = file.OpenRead();
        await using (stream.ConfigureAwait(false))
        {
            proto = IntermediateDar.Parser.ParseFrom(stream);
        }
        cancellationToken.ThrowIfCancellationRequested();

        var dar = IntermediateDarReader.Read(proto);
        LogPackage(logger, dar.MainPackage.Name, dar.MainPackage.Version);
        LogModuleCount(logger, dar.MainPackage.Modules.Count);
        LogDependencyCount(logger, dar.Dependencies.Count);

        var effectiveCounter = args.ReleaseCountersFile is not null
            ? ResolveReleaseCounter(args.ReleaseCountersFile, ResolveCodegenVersion(args), dar.MainPackage.Name, dar.MainPackage.Version, logger)
            : args.EmitterCounter;

        var generator = new CSharpCodeGenerator(BuildOptions(args, effectiveCounter), logger);
        var generatedFiles = generator.Generate(dar);
        await WriteGeneratedFiles(generatedFiles, args, logger, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveCodegenVersion(CodegenArgs args) =>
        string.IsNullOrWhiteSpace(args.CodegenVersion)
            ? ProjectFileGenerator.EmitterLockstepVersion
            : args.CodegenVersion;

    private static int ResolveReleaseCounter(
        FileInfo storeFile,
        string codegenVersion,
        string packageName,
        Version packageVersion,
        ILogger logger)
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(storeFile.FullName);
        var version = NuGetVersionResolver.Compute(packageVersion, codegenVersion, store);

        var threePartVersion = $"{packageVersion.Major}.{packageVersion.Minor}.{Math.Max(0, packageVersion.Build)}";
        var resolvedVersion = version.ToString();
        LogReleaseCounter(logger, codegenVersion, packageName, threePartVersion, resolvedVersion);

        return version.Generation;
    }

    private static CodeGenOptions BuildOptions(CodegenArgs args, int emitterCounter) =>
        new()
        {
            NamespacePrefix = args.NamespacePrefix,
            RootFilter = args.RootFilter,
            EnableNullableReferenceTypes = args.EnableNullable,
            GenerateProjectFile = args.GenerateProjectFile,
            IncludeDependencies = args.IncludeDependencies,
            TargetFramework = args.TargetFramework,
            RuntimePackageVersion = args.RuntimePackageVersion,
            EmitterCounter = emitterCounter,
            PackageLicenseExpression = args.PackageLicenseExpression,
            VersionSuffix = args.VersionSuffix,
            PublishesReferencedPackages = args.ReleaseCountersFile is not null,
            RepositoryUrl = args.RepositoryUrl,
        };

    private static async Task WriteGeneratedFiles(IReadOnlyList<GeneratedFile> generatedFiles, CodegenArgs args, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var file in generatedFiles)
        {
            var filePath = Path.Combine(args.OutputDirectory.FullName, file.RelativePath);
            var fileDir = Path.GetDirectoryName(filePath);
            if (fileDir is not null && !Directory.Exists(fileDir))
            {
                Directory.CreateDirectory(fileDir);
            }

            if (file.IsBinary)
            {
                await File.WriteAllBytesAsync(filePath, file.BinaryContent!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await File.WriteAllTextAsync(filePath, file.Content, cancellationToken).ConfigureAwait(false);
            }
            LogGeneratedFile(logger, file.RelativePath);
        }
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Daml C# Codegen v{AssemblyVersion}")]
    private static partial void LogBanner(ILogger logger, Version? assemblyVersion);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Output directory: {OutputDirectory}")]
    private static partial void LogOutputDirectory(ILogger logger, string outputDirectory);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Created output directory: {OutputDirectory}")]
    private static partial void LogCreatedOutputDirectory(ILogger logger, string outputDirectory);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Code generation was canceled. Partially written files may remain in '{OutputDirectory}'.")]
    private static partial void LogCanceled(ILogger logger, string outputDirectory);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error, Message = "Code generation failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Root cause: {RootCause}")]
    private static partial void LogRootCause(ILogger logger, string rootCause);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Error, Message = "{FailureDetail}")]
    private static partial void LogFailureDetail(ILogger logger, string failureDetail);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information, Message = "Reading IntermediateDar: {FileName}")]
    private static partial void LogReadingIntermediate(ILogger logger, string fileName);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information, Message = "  Package: {PackageName} v{PackageVersion}")]
    private static partial void LogPackage(ILogger logger, string packageName, Version packageVersion);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Information, Message = "  Modules: {ModuleCount}")]
    private static partial void LogModuleCount(ILogger logger, int moduleCount);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "  Dependencies: {DependencyCount}")]
    private static partial void LogDependencyCount(ILogger logger, int dependencyCount);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Information,
        Message = "  Release counter: codegen_version={CodegenVersion}; {PackageName} {PackageVersion} version={ResolvedVersion}")]
    private static partial void LogReleaseCounter(ILogger logger, string codegenVersion, string packageName, string packageVersion, string resolvedVersion);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug, Message = "  Generated: {RelativePath}")]
    private static partial void LogGeneratedFile(ILogger logger, string relativePath);
}

internal sealed record CodegenArgs(
    FileInfo IntermediateFile,
    DirectoryInfo OutputDirectory,
    string? NamespacePrefix,
    int Verbosity,
    string? RootFilter,
    bool EnableNullable,
    bool GenerateProjectFile,
    bool IncludeDependencies,
    string TargetFramework,
    string? RuntimePackageVersion,
    int EmitterCounter,
    FileInfo? ReleaseCountersFile,
    string? CodegenVersion,
    string PackageLicenseExpression,
    string? VersionSuffix,
    string? RepositoryUrl);
