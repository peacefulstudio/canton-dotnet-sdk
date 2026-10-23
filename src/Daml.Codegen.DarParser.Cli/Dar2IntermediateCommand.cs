// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using Google.Protobuf;
using Daml.Codegen.CSharp;
using Daml.Codegen.DarParser;
using Microsoft.Extensions.Logging;

namespace Daml.Codegen.DarParser.Cli;

/// <summary>
/// The pure-.NET <c>dar2intermediate</c> entry point: reads a <c>.dar</c> with
/// <c>Daml.Codegen.DarParser</c> and writes the corresponding
/// <c>IntermediateDar</c> protobuf to an <c>intermediate.binpb</c> file. This is
/// the JDK-free alternative to the JVM helper: the emitted
/// <c>.binpb</c> is consumed unchanged by <c>Daml.Codegen.CSharp.Cli --intermediate</c>.
/// </summary>
public static partial class Dar2IntermediateCommand
{
    /// <summary>
    /// Builds the <c>dar2intermediate</c> subcommand for the DAR-direct CLI.
    /// </summary>
    public static Command Create()
    {
        var command = new Command(
            "dar2intermediate",
            "Translate a DAR into an IntermediateDar protobuf (.binpb) without a JDK");

        var darFileArgument = new Argument<FileInfo>("dar-file")
        {
            Description = "DAR file to translate into an IntermediateDar protobuf"
        };
        darFileArgument.Validators.Add(result =>
        {
            var file = result.GetValueOrDefault<FileInfo>();
            if (file is null || !file.Exists)
            {
                result.AddError($"DAR file not found: {file?.FullName}");
                return;
            }
            if (!file.Extension.Equals(".dar", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError($"File must have .dar extension: {file.FullName}");
            }
        });

        var outputOption = new Option<FileInfo>("-o")
        {
            Description = "Output path for the IntermediateDar protobuf",
            Required = true
        };
        outputOption.Aliases.Add("--output");

        var verbosityOption = new Option<int>("-v")
        {
            Description = "Verbosity level: 0=errors only, 1=warnings, 2=info, 3=debug",
            DefaultValueFactory = _ => 1
        };
        verbosityOption.Aliases.Add("--verbosity");

        command.Arguments.Add(darFileArgument);
        command.Options.Add(outputOption);
        command.Options.Add(verbosityOption);

        command.SetAction((parseResult, cancellationToken) => Run(
            parseResult.GetValue(darFileArgument)!,
            parseResult.GetValue(outputOption)!,
            parseResult.GetValue(verbosityOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> Run(FileInfo darFile, FileInfo output, int verbosity, CancellationToken cancellationToken)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new VerbosityConsoleLoggerProvider(verbosity))
                   .SetMinimumLevel(LogLevel.Trace));
        var logger = loggerFactory.CreateLogger(nameof(Dar2IntermediateCommand));
        try
        {
            LogReadingDar(logger, darFile.Name);
            var dar = await DarArchive.ReadAsync(darFile.FullName, cancellationToken).ConfigureAwait(false);
            LogPackage(logger, dar.MainPackage.Name, dar.MainPackage.Version);
            LogModuleCount(logger, dar.MainPackage.Modules.Count);
            LogDependencyCount(logger, dar.Dependencies.Count);

            var proto = IntermediateDarWriter.Write(dar);

            var outputDirectory = output.Directory;
            if (outputDirectory is not null && !outputDirectory.Exists)
            {
                outputDirectory.Create();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stream = output.Create();
            await using (stream.ConfigureAwait(false))
            {
                proto.WriteTo(stream);
            }

            LogWroteIntermediate(logger, output.FullName);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCanceled(logger);
            if (output.Exists)
            {
                try
                {
                    output.Delete();
                    LogDeletedPartialOutput(logger, output.FullName);
                }
                catch (Exception cleanupError)
                {
                    LogPartialOutputMayRemain(logger, output.FullName, cleanupError.Message);
                }
            }
            return 130;
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex.Message);
            if (verbosity >= 3)
            {
                LogFailureDetail(logger, ex.ToString());
            }
            return 1;
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Reading DAR: {FileName}")]
    private static partial void LogReadingDar(ILogger logger, string fileName);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "  Package: {PackageName} v{PackageVersion}")]
    private static partial void LogPackage(ILogger logger, string packageName, Version packageVersion);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "  Modules: {ModuleCount}")]
    private static partial void LogModuleCount(ILogger logger, int moduleCount);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug, Message = "  Dependencies: {DependencyCount}")]
    private static partial void LogDependencyCount(ILogger logger, int dependencyCount);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Wrote IntermediateDar: {OutputPath}")]
    private static partial void LogWroteIntermediate(ILogger logger, string outputPath);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error, Message = "dar2intermediate was canceled.")]
    private static partial void LogCanceled(ILogger logger);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "Deleted partial output at '{OutputPath}'.")]
    private static partial void LogDeletedPartialOutput(ILogger logger, string outputPath);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "A partial output file may remain at '{OutputPath}': {Reason}")]
    private static partial void LogPartialOutputMayRemain(ILogger logger, string outputPath, string reason);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Error, Message = "dar2intermediate failed: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Error, Message = "{FailureDetail}")]
    private static partial void LogFailureDetail(ILogger logger, string failureDetail);
}
