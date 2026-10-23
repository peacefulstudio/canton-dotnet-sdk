// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate;
using Google.Protobuf;

namespace Daml.Codegen.CSharp.Cli;

internal static class IntermediateDarFile
{
    internal const int MaximumProtobufNestingLevels = 1024;

    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    internal static async Task<IntermediateDar> ReadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        var stream = file.OpenRead();
        await using (stream.ConfigureAwait(false))
        {
            await RejectDarArchive(file, stream, cancellationToken).ConfigureAwait(false);
            try
            {
                var input = CodedInputStream.CreateWithLimits(stream, int.MaxValue, MaximumProtobufNestingLevels);
                return IntermediateDar.Parser.ParseFrom(input);
            }
            catch (InvalidProtocolBufferException ex)
            {
                throw new InvalidDataException(
                    $"{file.Name} is not a valid intermediate DAR: {ex.Message} "
                    + "Expected the protobuf written by `dar2intermediate` or `dpm codegen-cs`; "
                    + $"nesting is capped at {MaximumProtobufNestingLevels} protobuf levels.",
                    ex);
            }
        }
    }

    private static async Task RejectDarArchive(FileInfo file, Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[ZipMagic.Length];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);
        if (read == header.Length && header.AsSpan().SequenceEqual(ZipMagic))
        {
            throw new InvalidDataException(
                $"{file.Name} is a DAR archive, not an intermediate DAR. "
                + "Convert it first with `Daml.Codegen.DarParser.Cli dar2intermediate`, "
                + "or run the whole pipeline in one step with `dpm codegen-cs`.");
        }
    }
}
