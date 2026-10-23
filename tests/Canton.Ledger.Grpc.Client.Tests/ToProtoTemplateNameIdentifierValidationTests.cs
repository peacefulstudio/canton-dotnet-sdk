// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Canton.Ledger.Grpc.Client.Tests;

public sealed class ToProtoTemplateNameIdentifierValidationTests
{
    [Fact]
    public void Rejects_a_package_name_that_already_carries_the_by_package_name_prefix()
    {
        var identifier = new RuntimeIdentifier("9b63deadbeefhash", "RichTypes", "RichRecord");

        var converting = () => DamlValueConverter.ToProtoTemplateNameIdentifier("#richtypes", identifier);

        converting.Should().Throw<ArgumentException>()
            .WithMessage("*'#richtypes' already carries the '#'*")
            .And.ParamName.Should().Be("packageName");
    }
}
