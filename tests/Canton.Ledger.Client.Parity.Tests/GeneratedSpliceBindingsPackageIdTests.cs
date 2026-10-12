// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Splice.ValidatorLicense;
using Splice.Wallet.TransferOffer;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

public sealed class GeneratedSpliceBindingsPackageIdTests
{
    [Fact]
    public void ValidatorLicense_generated_binding_carries_the_splice_amulet_package_id_that_the_LocalNet_Splice_deploys()
    {
        ValidatorLicense.PackageId.Should().Be("17b6f7c18efec9022dde39a19039783c68490a722e8a5088b22ee034678171a2");
        ValidatorLicense.PackageName.Should().Be("splice-amulet");
    }

    [Fact]
    public void TransferOffer_generated_binding_carries_the_splice_wallet_package_id_that_the_LocalNet_Splice_deploys()
    {
        TransferOffer.PackageId.Should().Be("916f16ea9413ba72d9ae660c9d0741f1078fc27b82b61e4b9db6a991dc47a848");
        TransferOffer.PackageName.Should().Be("splice-wallet");
    }
}
