// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Runtime;

internal readonly record struct StreamErrorFault(
    TransportStatus Status,
    string Message,
    DamlErrorCategory? Category,
    string? ErrorId);
