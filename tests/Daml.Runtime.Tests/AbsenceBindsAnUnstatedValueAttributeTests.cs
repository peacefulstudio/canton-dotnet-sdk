// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Runtime.Serialization;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class AbsenceBindsAnUnstatedValueAttributeTests
{
    [Fact]
    public void AbsenceBindsAnUnstatedValueAttribute_enrols_exactly_the_runtime_types_whose_absence_binds_a_value_the_slot_forbids()
    {
        var enrolled = typeof(DamlJsonConverters).Assembly.GetTypes()
            .Where(type => type.IsDefined(typeof(AbsenceBindsAnUnstatedValueAttribute), inherit: false))
            .Select(type => type.FullName);

        enrolled.Should().BeEquivalentTo(
        [
            "Daml.Runtime.Contracts.EquatableArray`1",
            "Daml.Runtime.LedgerOffset",
            "Daml.Runtime.Outcomes.TransportStatus",
            "Daml.Runtime.Stdlib.Set`1",
            "Daml.Runtime.Stdlib.Map`2",
            "Daml.Runtime.Stdlib.NonEmpty`1",
        ]);
    }

    [Fact]
    public void AbsenceBindsAnUnstatedValueAttribute_is_not_inherited_by_the_arms_of_an_enrolled_type()
    {
        typeof(AbsenceBindsAnUnstatedValueAttribute).GetCustomAttribute<AttributeUsageAttribute>()!
            .Inherited.Should().BeFalse();
    }
}
