// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Commands;

internal static class IdMinting
{
    internal static string Mint() => Guid.NewGuid().ToString();

    internal static CommandId MintCommandId() => new(Mint());
}
