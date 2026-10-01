// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Xunit;

namespace Canton.Ledger.Pqs.Client.Tests;

public class SqlFragmentTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1leadingDigit")]
    [InlineData("has-dash")]
    [InlineData("has.dot")]
    [InlineData("has space")]
    [InlineData("has;semicolon")]
    [InlineData("has'quote")]
    [InlineData("has\"doublequote")]
    [InlineData("has\\backslash")]
    [InlineData("has/slash")]
    [InlineData("has(paren")]
    [InlineData("name; DROP TABLE active; --")]
    public void JsonKey_throws_for_unsafe_field_name(string fieldName)
    {
        var act = () => SqlFragment.JsonKey(fieldName);

        act.Should().Throw<ArgumentException>().WithMessage($"*'{fieldName}'*");
    }

    [Theory]
    [InlineData("a", "'a'")]
    [InlineData("Z", "'Z'")]
    [InlineData("_underscore", "'_underscore'")]
    [InlineData("camelCase", "'camelCase'")]
    [InlineData("PascalCase", "'PascalCase'")]
    [InlineData("with_underscores", "'with_underscores'")]
    [InlineData("name123", "'name123'")]
    [InlineData("name_123_456", "'name_123_456'")]
    public void JsonKey_quotes_a_safe_field_name(string fieldName, string expected)
    {
        Render(SqlFragment.JsonKey(fieldName)).Sql.Should().Be(expected);
    }

    [Fact]
    public void Render_numbers_bound_values_from_the_running_index()
    {
        var fragment = SqlFragment.Of($"{SqlFragment.Parameter("a")} < {SqlFragment.Parameter(7L)}");
        var parameters = new List<(string Name, object Value)>();
        var paramIndex = 3;

        var sql = fragment.Render(parameters, ref paramIndex);

        sql.Should().Be("@p3 < @p4");
        parameters.Should().Equal(("@p3", "a"), ("@p4", 7L));
        paramIndex.Should().Be(5);
    }

    [Fact]
    public void Render_never_inlines_a_bound_value()
    {
        const string nasty = "x'; DROP TABLE active; --";

        var (sql, parameters) = Render(SqlFragment.Of($"payload->>'a' = {SqlFragment.Parameter(nasty)}"));

        sql.Should().Be("payload->>'a' = @p0");
        parameters.Should().Equal(("@p0", nasty));
    }

    [Fact]
    public void Alias_renders_prefix_and_index()
    {
        Render(SqlFragment.Alias('e', 12)).Sql.Should().Be("e12");
    }

    private static (string Sql, List<(string Name, object Value)> Parameters) Render(SqlFragment fragment)
    {
        var parameters = new List<(string Name, object Value)>();
        var paramIndex = 0;
        return (fragment.Render(parameters, ref paramIndex), parameters);
    }
}
