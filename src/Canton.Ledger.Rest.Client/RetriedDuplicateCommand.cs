// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Canton.Ledger.Rest.Client;

internal static class RetriedDuplicateCommand
{
    internal const string ErrorId = "DUPLICATE_COMMAND";

    private const string AcceptedMetadataKey = "accepted";
    private const string CompletionOffsetMetadataKey = "completion_offset";

    internal static bool TryReadCompletionOffset(
        IReadOnlyDictionary<string, string> metadata, out long completionOffset)
    {
        completionOffset = 0;
        if (OriginalWasNotAccepted(metadata)
            || !metadata.TryGetValue(CompletionOffsetMetadataKey, out var rawCompletionOffset)
            || !long.TryParse(rawCompletionOffset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0)
        {
            return false;
        }

        completionOffset = parsed;
        return true;
    }

    private static bool OriginalWasNotAccepted(IReadOnlyDictionary<string, string> metadata) =>
        metadata.TryGetValue(AcceptedMetadataKey, out var accepted)
        && string.Equals(accepted, "false", StringComparison.OrdinalIgnoreCase);
}
