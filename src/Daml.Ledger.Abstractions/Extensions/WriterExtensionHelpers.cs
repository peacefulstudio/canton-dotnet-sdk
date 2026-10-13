// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime.Commands;

namespace Daml.Ledger.Abstractions.Extensions;

internal static class WriterExtensionHelpers
{
    internal static void ThrowIfDefault(CommandId? commandId)
    {
        if (commandId == default(CommandId))
        {
            throw new ArgumentException(
                "A default (uninitialized) CommandId cannot be submitted. Construct one from a non-empty string, or omit the argument to have a fresh id minted.",
                nameof(commandId));
        }
    }

    internal static CommandId CommandIdOrMint(CommandId? commandId)
    {
        ThrowIfDefault(commandId);
        return commandId ?? new CommandId(Guid.NewGuid().ToString());
    }
}
