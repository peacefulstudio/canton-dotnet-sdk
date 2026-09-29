// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Data;
using NSubstitute;
using Xunit;

#pragma warning disable xUnit1051

namespace Canton.Ledger.Abstractions.Tests;

/// <summary>
/// Proves every pre-existing <c>IAdminClient.UploadDarAsync</c> / <c>IAdminClient.ValidateDarAsync</c>
/// call shape still binds to the original overload once the <c>synchronizerId</c> overloads exist alongside
/// them, and that a call supplying a <see cref="SynchronizerId"/> binds to the new overload instead.
/// </summary>
public class AdminClientDarOverloadResolutionTests
{
    private static readonly byte[] DarFile = [0x50, 0x4B, 0x03, 0x04];
    private static readonly SynchronizerId Synchronizer = new("global-domain::1220ff");

    [Fact]
    public async Task UploadDarAsync_positional_submissionId_and_token_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, "sub-1", CancellationToken.None);

        await admin.Received(1).UploadDarAsync(DarFile, "sub-1", CancellationToken.None);
    }

    [Fact]
    public async Task UploadDarAsync_named_token_skipping_submissionId_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, cancellationToken: CancellationToken.None);

        await admin.Received(1).UploadDarAsync(DarFile, null, CancellationToken.None);
    }

    [Fact]
    public async Task UploadDarAsync_default_token_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, "sub-1", default);

        await admin.Received(1).UploadDarAsync(DarFile, "sub-1", default);
    }

    [Fact]
    public async Task UploadDarAsync_default_literal_as_second_argument_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, default);

        await admin.Received(1).UploadDarAsync(DarFile, null, CancellationToken.None);
    }

    [Fact]
    public async Task UploadDarAsync_omitting_every_optional_argument_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile);

        await admin.Received(1).UploadDarAsync(DarFile, null, CancellationToken.None);
    }

    [Fact]
    public async Task UploadDarAsync_with_a_synchronizerId_binds_to_the_new_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, Synchronizer, null);

        await admin.Received(1).UploadDarAsync(DarFile, Synchronizer, null, CancellationToken.None);
    }

    [Fact]
    public async Task UploadDarAsync_with_a_synchronizerId_submissionId_and_token_binds_to_the_new_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.UploadDarAsync(DarFile, Synchronizer, "sub-1", CancellationToken.None);

        await admin.Received(1).UploadDarAsync(DarFile, Synchronizer, "sub-1", CancellationToken.None);
    }

    [Fact]
    public async Task ValidateDarAsync_positional_token_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.ValidateDarAsync(DarFile, CancellationToken.None);

        await admin.Received(1).ValidateDarAsync(DarFile, CancellationToken.None);
    }

    [Fact]
    public async Task ValidateDarAsync_default_token_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.ValidateDarAsync(DarFile, default);

        await admin.Received(1).ValidateDarAsync(DarFile, default);
    }

    [Fact]
    public async Task ValidateDarAsync_omitting_the_token_binds_to_the_original_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.ValidateDarAsync(DarFile);

        await admin.Received(1).ValidateDarAsync(DarFile, CancellationToken.None);
    }

    [Fact]
    public async Task ValidateDarAsync_with_a_synchronizerId_binds_to_the_new_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.ValidateDarAsync(DarFile, Synchronizer);

        await admin.Received(1).ValidateDarAsync(DarFile, Synchronizer, CancellationToken.None);
    }

    [Fact]
    public async Task ValidateDarAsync_with_a_synchronizerId_and_named_token_binds_to_the_new_overload()
    {
        var admin = Substitute.For<IAdminClient>();

        await admin.ValidateDarAsync(DarFile, Synchronizer, cancellationToken: CancellationToken.None);

        await admin.Received(1).ValidateDarAsync(DarFile, Synchronizer, CancellationToken.None);
    }
}
