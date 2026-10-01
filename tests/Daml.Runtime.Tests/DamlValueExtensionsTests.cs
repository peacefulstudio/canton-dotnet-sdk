// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlValueExtensionsTests
{
    [Fact]
    public void FromDamlValue_should_convert_DamlInt64_to_long()
    {
        new DamlInt64(42).FromDamlValue<long>().Should().Be(42L);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlBool_to_bool()
    {
        new DamlBool(true).FromDamlValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlNumeric_to_decimal()
    {
        new DamlNumeric(3.14m).FromDamlValue<decimal>().Should().Be(3.14m);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlDate_to_DateOnly()
    {
        var date = new DateOnly(2024, 6, 15);
        new DamlDate(date).FromDamlValue<DateOnly>().Should().Be(date);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlTimestamp_to_DateTimeOffset()
    {
        var ts = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        new DamlTimestamp(ts).FromDamlValue<DateTimeOffset>().Should().Be(ts);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlText_to_string()
    {
        new DamlText("hello").FromDamlValue<string>().Should().Be("hello");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_string()
    {
        new DamlParty("party::alice").FromDamlValue<string>().Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlContractId_to_string()
    {
        new DamlContractId("00abc").FromDamlValue<string>().Should().Be("00abc");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_Party_value_type()
    {
        var result = new DamlParty("party::alice").FromDamlValue<Party>();

        result.Value.Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlContractId_to_typed_ContractId()
    {
        var cid = new DamlContractId("00abc123");

        var result = cid.FromDamlValue<ContractId<TestTemplate>>();

        result.Should().NotBeNull();
        result!.Value.Should().Be("00abc123");
    }

    [Fact]
    public void FromDamlValue_should_return_same_instance_when_target_is_runtime_type()
    {
        var text = new DamlText("hello");

        text.FromDamlValue<DamlText>().Should().BeSameAs(text);
    }

    [Fact]
    public void FromDamlValue_should_return_same_instance_when_target_is_DamlValue()
    {
        var text = new DamlText("hello");

        text.FromDamlValue<DamlValue>().Should().BeSameAs(text);
    }

    [Fact]
    public void FromDamlValue_should_throw_for_unsupported_target_type()
    {
        var action = () => new DamlText("nope").FromDamlValue<DateTime>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_throw_for_unsupported_value_to_string()
    {
        var action = () => new DamlInt64(42).FromDamlValue<string>();

        action.Should().Throw<NotSupportedException>();
    }

    // ── Unit → default(T) ───────────────────────────────────────────

    [Fact]
    public void FromDamlValue_should_throw_when_unwrapping_Unit_to_non_nullable_value_type()
    {
        var action = () => DamlUnit.Instance.FromDamlValue<long>();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void FromDamlValue_should_return_null_when_unwrapping_Unit_to_reference_type()
    {
        DamlUnit.Instance.FromDamlValue<string>().Should().BeNull();
    }

    [Fact]
    public void FromDamlValue_should_return_unit_instance_when_target_is_DamlUnit()
    {
        DamlUnit.Instance.FromDamlValue<DamlUnit>().Should().BeSameAs(DamlUnit.Instance);
    }

    [Fact]
    public void FromDamlValue_should_return_unit_instance_when_target_is_object()
    {
        // Assignable check takes precedence over the DamlUnit → default path:
        // object is assignable from DamlUnit, so we return the singleton rather than null.
        DamlUnit.Instance.FromDamlValue<object>().Should().BeSameAs(DamlUnit.Instance);
    }

    // ── Nullable<T> primitive branches (regression for asymmetry bug) ──

    [Fact]
    public void FromDamlValue_should_convert_DamlInt64_to_nullable_long()
    {
        new DamlInt64(42).FromDamlValue<long?>().Should().Be(42L);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlBool_to_nullable_bool()
    {
        new DamlBool(true).FromDamlValue<bool?>().Should().BeTrue();
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlNumeric_to_nullable_decimal()
    {
        new DamlNumeric(3.14m).FromDamlValue<decimal?>().Should().Be(3.14m);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlDate_to_nullable_DateOnly()
    {
        var date = new DateOnly(2024, 6, 15);
        new DamlDate(date).FromDamlValue<DateOnly?>().Should().Be(date);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlTimestamp_to_nullable_DateTimeOffset()
    {
        var ts = DateTimeOffset.UnixEpoch.AddSeconds(1704067200);
        new DamlTimestamp(ts).FromDamlValue<DateTimeOffset?>().Should().Be(ts);
    }

    public static TheoryData<string, DamlOptional> ExistingOptionals() => new()
    {
        { "None", DamlOptional.None },
        { "Some", DamlOptional.Some(new DamlInt64(42)) },
    };

    [Theory]
    [MemberData(nameof(ExistingOptionals))]
    public void AsOptional_returns_existing_DamlOptional_unchanged(string shape, DamlOptional optional)
    {
        optional.AsOptional().Should().Be(optional, "an existing {0} optional must pass through untouched", shape);
    }

    [Fact]
    public void AsOptional_wraps_bare_value_as_Some()
    {
        new DamlInt64(42).AsOptional().Should().Be(DamlOptional.Some(new DamlInt64(42)));
    }

    [Fact]
    public void AsOptional_reads_a_DamlOptionalChain_as_the_DamlOptional_level_it_is_instead_of_wrapping_it_as_Some()
    {
        DamlOptionalChain.Some(new DamlText("deep")).AsOptional()
            .Should().Be(DamlOptional.Some(new DamlText("deep")));
        DamlOptionalChain.None.AsOptional().Should().Be(DamlOptional.None);
    }

    [Fact]
    public void FromDamlValue_should_convert_DamlParty_to_nullable_Party()
    {
        var result = new DamlParty("party::alice").FromDamlValue<Party?>();

        result.Should().NotBeNull();
        result!.Value.Value.Should().Be("party::alice");
    }

    [Fact]
    public void FromDamlValue_should_return_null_when_unwrapping_Unit_to_nullable_primitive()
    {
        DamlUnit.Instance.FromDamlValue<long?>().Should().BeNull();
    }

    internal sealed record TestTemplate(string Owner) : ITemplate
    {
        public static Identifier TemplateId { get; } = new("pkg", "Module", "Template");
        public static string PackageId => "pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() =>
            new(null, [new DamlField(nameof(Owner), new DamlParty(Owner))]);
    }
}
