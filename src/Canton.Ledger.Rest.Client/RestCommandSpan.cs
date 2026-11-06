// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;

namespace Canton.Ledger.Rest.Client;

internal sealed record RestCommandSpan(CommandsSubmission Submission, bool EmitIdentifiers, long MaxResponseBodyBytes,
    TimeSpan? BodyWindow = null);
