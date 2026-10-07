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
        ValidatorLicense.PackageId.Should().Be("8fe7573f5535dc5b910a1f24d7d980da0e10ab38f8d29cb397146119bbfb7b3a");
        ValidatorLicense.PackageName.Should().Be("splice-amulet");
    }

    [Fact]
    public void TransferOffer_generated_binding_carries_the_splice_wallet_package_id_that_the_LocalNet_Splice_deploys()
    {
        TransferOffer.PackageId.Should().Be("d42261314d98cc04bf50bcc58930af98b9f33aeed11339465925de697d3ba66e");
        TransferOffer.PackageName.Should().Be("splice-wallet");
    }
}
