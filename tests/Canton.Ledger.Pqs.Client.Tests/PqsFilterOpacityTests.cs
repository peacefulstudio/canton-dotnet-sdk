// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class PqsFilterOpacityTests
{
    [Fact]
    public void ToString_reveals_no_field_name_or_value()
    {
        PqsFilter[] filters =
        [
            Filter.Where<RichRecord>(r => r.Label == "secret-label"),
            Filter.Where<RichRecord>(r => !r.Tags.Contains("hidden-tag")),
            Filter.Where<RichRecord>(r => r.Note == null),
            Filter.Where<RichRecord>(r => r.Attributes.ContainsKey("tier")),
            Filter.Field<RichRecord>(r => r.Amount, "42.5"),
        ];

        foreach (var filter in filters)
            filter.ToString().Should().NotContainAny("label", "secret-label", "tags", "hidden-tag", "note", "attributes", "tier", "amount", "42.5");
    }

    [Fact]
    public void Equals_does_not_match_two_filters_built_from_the_same_predicate()
    {
        var first = Filter.Where<RichRecord>(r => r.Count == 5);
        var second = Filter.Where<RichRecord>(r => r.Count == 5);

        first.Should().NotBe(second);
    }
}
