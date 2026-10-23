// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Daml.Codegen.CSharp.CodeGen;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class IdentifiersTests
{
    [Theory]
    [InlineData("Period", "Period", "Period_")]
    [InlineData("Other", "Period", "Other")]
    [InlineData("Period", "Other", "Period")]
    [InlineData("Period", "", "Period")]
    public void Disambiguate_appends_underscore_only_when_identifier_equals_enclosing_type(
        string identifier, string enclosingTypeName, string expected)
    {
        Identifiers.Disambiguate(identifier, enclosingTypeName).Should().Be(expected);
    }

    [Theory]
    [InlineData("period", "Period", "Period_")]
    [InlineData("other", "Period", "Other")]
    [InlineData("period", "", "Period")]
    [InlineData("operator", "Agreement", "Operator")]
    [InlineData("lock", "HoldingView", "Lock")]
    [InlineData("lock", "Lock", "Lock_")]
    public void MemberName_pascal_cases_then_disambiguates_against_enclosing_type(
        string damlFieldName, string enclosingTypeName, string expected)
    {
        Identifiers.MemberName(damlFieldName, enclosingTypeName).Should().Be(expected);
    }

    [Fact]
    public void MemberName_known_limitation_field_named_period_underscore_collides_with_disambiguated_Period()
    {
        Identifiers.MemberName("period_", "Period").Should().Be("Period_");
    }

    [Theory]
    [InlineData("Iou", true, null, "Iou")]
    [InlineData("Splice.Testing.Tokens.TestTokenV1", true, null, "Splice.Testing.Tokens.TestTokenV1")]
    [InlineData("Daml.Finance.Holding", true, null, "Daml.Finance.Holding")]
    [InlineData("Splice.Amulet", true, "Acme.Ledger", "Acme.Ledger.Splice.Amulet")]
    [InlineData("Acme.Ledger.Token", true, "Acme.Ledger", "Acme.Ledger.Token")]
    [InlineData("Acme.Ledger", true, "Acme.Ledger", "Acme.Ledger")]
    [InlineData("Acme.LedgerX", true, "Acme.Ledger", "Acme.Ledger.Acme.LedgerX")]
    [InlineData("Ledger.Token", true, "Acme.Ledger", "Acme.Ledger.Ledger.Token")]
    [InlineData("Splice.Amulet", false, "Acme.Ledger", "Splice.Amulet")]
    [InlineData("Acme.Ledger.Token", false, "Acme.Ledger", "Acme.Ledger.Token")]
    [InlineData("RichTypes", true, "Daml.Codegen.Testing.Conformance", "Daml.Codegen.Testing.Conformance.RichTypes")]
    public void ModuleNamespace_is_the_module_name_prefixed_only_for_the_main_package_and_only_when_it_does_not_already_start_with_the_prefix(
        string moduleName, bool isMainPackage, string? namespacePrefix, string expected)
    {
        var options = new CodeGenOptions { NamespacePrefix = namespacePrefix };

        Identifiers.ModuleNamespace(moduleName, isMainPackage, options).Should().Be(expected);
    }

    [Theory]
    [InlineData("Foo$u0027.Bar", "Foo_u0027.Bar")]
    [InlineData("A.0x", "A._u0030x")]
    public void ModuleNamespace_sanitises_each_segment_and_keeps_the_dots(string moduleName, string expected)
    {
        Identifiers.ModuleNamespace(moduleName, isMainPackage: true, new CodeGenOptions()).Should().Be(expected);
    }

    [Fact]
    public void InterfaceMarkerName_prefixes_the_sanitized_name_with_i_when_unreserved()
    {
        Identifiers.InterfaceMarkerName("Holding", new HashSet<string>()).Should().Be("IHolding");
    }

    [Fact]
    public void InterfaceMarkerName_appends_underscore_when_a_template_reserves_the_marker()
    {
        Identifiers.InterfaceMarkerName("Factory", new HashSet<string> { "IFactory" }).Should().Be("IFactory_");
    }

    [Fact]
    public void InterfaceMarkerName_keeps_appending_underscores_until_the_marker_is_unreserved()
    {
        Identifiers.InterfaceMarkerName("Factory", new HashSet<string> { "IFactory", "IFactory_" }).Should().Be("IFactory__");
    }

    [Theory]
    [InlineData("Foo$u0027", "Foo_u0027")]
    [InlineData("Foo_u0027", "Foo__u0027")]
    [InlineData("foo_bar", "foo_bar")]
    [InlineData("AnsEntry_Expire", "AnsEntry_Expire")]
    [InlineData("foo__u0027", "foo____u0027")]
    [InlineData("_u0024", "__u0024")]
    [InlineData("Void$u0023", "Void_u0023")]
    [InlineData("Void$u00232", "Void_u00232")]
    [InlineData("Foo$u003a", "Foo_u003a")]
    [InlineData("0x", "_u0030x")]
    public void SanitizeBare_injectively_escapes_the_mangled_name(string mangled, string expected)
    {
        Identifiers.SanitizeBare(mangled).Should().Be(expected);
    }

    [Theory]
    [InlineData("Smile$U0001f642", "Smile_ud83d_ude42")]
    [InlineData("Smile$$U0001f642", "Smile_u0024U0001f642")]
    [InlineData("$u003a$U0001f642$u003a", "_u003a_ud83d_ude42_u003a")]
    [InlineData("Bad$U0000d800", "Bad_u0024U0000d800")]
    [InlineData("Bad$U00110000", "Bad_u0024U00110000")]
    public void SanitizeBare_decodes_the_eight_digit_escape_damlc_emits_for_astral_code_points(string mangled, string expected)
    {
        Identifiers.SanitizeBare(mangled).Should().Be(expected);
    }

    [Fact]
    public void SanitizeBare_keeps_the_astral_escape_and_the_escaped_dollar_spelling_apart()
    {
        Identifiers.SanitizeBare("Smile$U0001f642").Should().NotBe(Identifiers.SanitizeBare("Smile$$U0001f642"));
    }

    [Fact]
    public void SanitizeBare_uppercase_dollar_u_escape_is_not_decoded_damlc_always_emits_lowercase_hex()
    {
        Identifiers.SanitizeBare("$u004A").Should().Be("_u0024u004A");
    }

    [Theory]
    [InlineData("Outcome.Win", "Outcome_Win")]
    [InlineData("Tree.Node", "Tree_Node")]
    [InlineData("Outcome.Win$u0027", "Outcome_Win_u0027")]
    public void Sanitize_joins_structural_dots_in_compound_variant_payload_names_with_underscore(
        string compoundName, string expected)
    {
        Identifiers.Sanitize(compoundName).Should().Be(expected);
    }

    [Fact]
    public void SanitizeBare_is_injective_and_round_trips_across_the_damlc_mangler_domain()
    {
        var encodedToSource = new Dictionary<string, string>();
        var collisions = new List<string>();
        var roundTripFailures = new List<string>();

        foreach (var name in ManglerDomainNames(6))
        {
            var encoded = Identifiers.SanitizeBare(name);
            var decoded = Decode(encoded);
            if (decoded != name)
            {
                roundTripFailures.Add($"{name} -> {encoded} -> {decoded}");
            }

            if (encodedToSource.TryGetValue(encoded, out var prior) && prior != name)
            {
                collisions.Add($"{prior} and {name} both -> {encoded}");
            }
            encodedToSource[encoded] = name;
        }

        roundTripFailures.Should().BeEmpty();
        collisions.Should().BeEmpty();
    }

    private static IEnumerable<string> ManglerDomainNames(int maxLength)
    {
        const string alphabet = "_ua0'#.x27";
        var current = new List<string> { string.Empty };
        for (var length = 1; length <= maxLength; length++)
        {
            var next = new List<string>(current.Count * alphabet.Length);
            foreach (var prefix in current)
            {
                foreach (var c in alphabet)
                {
                    next.Add(prefix + c);
                }
            }
            current = next;
            foreach (var name in current)
            {
                if (!char.IsAsciiDigit(name[0]))
                {
                    yield return name;
                }
            }
        }
    }

    private static string Decode(string encoded)
    {
        var decoded = new StringBuilder(encoded.Length);
        var i = 0;
        while (i < encoded.Length)
        {
            if (encoded[i] == '_' && i + 1 < encoded.Length && encoded[i + 1] == '_')
            {
                decoded.Append('_');
                i += 2;
            }
            else if (encoded[i] == '_' && i + 5 < encoded.Length && encoded[i + 1] == 'u'
                && IsLowerHexDigit(encoded[i + 2]) && IsLowerHexDigit(encoded[i + 3])
                && IsLowerHexDigit(encoded[i + 4]) && IsLowerHexDigit(encoded[i + 5]))
            {
                decoded.Append((char)Convert.ToInt32(encoded.Substring(i + 2, 4), 16));
                i += 6;
            }
            else
            {
                decoded.Append(encoded[i]);
                i++;
            }
        }
        return decoded.ToString();
    }

    private static bool IsLowerHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';
}
