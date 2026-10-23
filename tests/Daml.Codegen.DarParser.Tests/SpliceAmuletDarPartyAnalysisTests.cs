// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using Daml.Codegen.DarParser;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.DarParser.Tests;

/// <summary>
/// Ground-truth coverage against a real, committed Splice DAR: this SDK's
/// generic-template desugaring compiles both an unadorned <c>observer</c>
/// clause and a <c>signatory &lt;field&gt;</c> clause through a
/// dictionary-method indirection that <see cref="PartyExpressionAnalyzer"/>
/// resolves. <c>AnsRules</c>'s observers resolve to
/// <see cref="DamlPartySource.Static"/> with an empty party list, and its
/// signatories to the <c>dso</c> payload field, rather than to
/// <see cref="DamlPartySource.Dynamic"/>.
/// </summary>
public sealed class SpliceAmuletDarPartyAnalysisTests
{
    private static async Task<DarArchive> LoadAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "splice-amulet-name-service.dar");
        return await DarArchive.ReadAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnsRules_observers_resolve_static_empty_through_dictionary_method_indirection()
    {
        var archive = await LoadAsync();

        var ansRules = archive.MainPackage.Modules
            .SelectMany(m => m.Templates)
            .Single(t => t.Name == "AnsRules");

        ansRules.Observers.Source.Should().Be(DamlPartySource.Static);
        ansRules.Observers.Parties.Should().BeEmpty();
    }

    [Fact]
    public async Task AnsRules_signatories_resolve_to_the_dso_field_through_the_let_bound_to_parties_wrapper()
    {
        var archive = await LoadAsync();

        var ansRules = archive.MainPackage.Modules
            .SelectMany(m => m.Templates)
            .Single(t => t.Name == "AnsRules");

        ansRules.Signatories.Source.Should().Be(DamlPartySource.Static);
        ansRules.Signatories.Parties.Should().ContainSingle()
            .Which.Should().BeOfType<DamlPartyPayloadField>()
            .Which.FieldName.Should().Be("dso");
    }
}
