// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Conformance.Probe;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Testing.Conformance;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Xunit;

namespace Canton.Ledger.Testing.LiveConformance;

/// <summary>
/// The conformance kit run against a seeded LocalNet participant. A transport subclasses it with
/// the fixture that reaches the participant over its wire.
/// </summary>
public abstract class LiveConformanceTests : LedgerClientConformanceTests<ProbeContract>
{
    private readonly LiveConformanceFixture _fixture;

    /// <summary>Initializes the class, skipping every check when no LocalNet participant is reachable.</summary>
    /// <param name="fixture">The seeded participant the checks run against.</param>
    protected LiveConformanceTests(LiveConformanceFixture fixture)
    {
        Assert.SkipUnless(fixture.IsAvailable, LiveConformanceFixture.SkipMessage);
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override SubmitterInfo Reader => _fixture.Reader;

    /// <inheritdoc />
    protected override LedgerOffset EmptySnapshotOffset => _fixture.EmptySnapshotOffset;

    /// <inheritdoc />
    protected override ILedgerClient CreateClient() => _fixture.CreateClient();

    /// <inheritdoc />
    protected override ILedgerClient? CreateFaultingSnapshotClient() => _fixture.CreateFaultingSnapshotClient();

    /// <inheritdoc />
    protected override WriteConformanceFixture? CreateWriteFixture() => _fixture.CreateWriteFixture();

    /// <inheritdoc />
    protected override CommandIdConformanceFixture? CreateCommandIdFixture() => _fixture.CreateCommandIdFixture();
}
