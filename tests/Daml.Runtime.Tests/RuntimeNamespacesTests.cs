// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class RuntimeNamespacesTests
{
    [Fact]
    public void RuntimeNamespaces_type_is_internal() =>
        typeof(RuntimeNamespaces).IsPublic.Should().BeFalse(
            "only the C# emitter reads these namespace literals, through the InternalsVisibleTo " +
            "grant on Daml.Runtime.csproj, so the type carries no public-surface commitment");

    [Fact]
    public void RuntimeNamespaces_Data_matches_the_pinned_literal() =>
        RuntimeNamespaces.Data.Should().Be("Daml.Runtime.Data");

    [Fact]
    public void RuntimeNamespaces_Contracts_matches_the_pinned_literal() =>
        RuntimeNamespaces.Contracts.Should().Be("Daml.Runtime.Contracts");

    [Fact]
    public void RuntimeNamespaces_Commands_matches_the_pinned_literal() =>
        RuntimeNamespaces.Commands.Should().Be("Daml.Runtime.Commands");

    [Fact]
    public void RuntimeNamespaces_Stdlib_matches_the_pinned_literal() =>
        RuntimeNamespaces.Stdlib.Should().Be("Daml.Runtime.Stdlib");

    [Fact]
    public void RuntimeNamespaces_Outcomes_matches_the_pinned_literal() =>
        RuntimeNamespaces.Outcomes.Should().Be("Daml.Runtime.Outcomes");
}
