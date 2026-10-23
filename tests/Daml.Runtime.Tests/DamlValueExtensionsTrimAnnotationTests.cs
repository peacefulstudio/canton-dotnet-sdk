// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlValueExtensionsTrimAnnotationTests
{
    private static MethodInfo FromDamlValueMethod() =>
        typeof(DamlValueExtensions).GetMethod(nameof(DamlValueExtensions.FromDamlValue))!;

    [Fact]
    public void FromDamlValue_requires_unreferenced_code_because_it_reflects_over_the_target_type()
    {
        FromDamlValueMethod().GetCustomAttribute<RequiresUnreferencedCodeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void FromDamlValue_requires_dynamic_code_because_it_closes_generic_methods_at_run_time()
    {
        FromDamlValueMethod().GetCustomAttribute<RequiresDynamicCodeAttribute>().Should().NotBeNull();
    }
}
