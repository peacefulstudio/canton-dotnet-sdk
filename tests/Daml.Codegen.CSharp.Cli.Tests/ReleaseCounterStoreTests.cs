// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Codegen.CSharp.Cli.Versioning;
using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

public class ReleaseCounterStoreTests : IDisposable
{
    private readonly string _storePath;

    public ReleaseCounterStoreTests()
    {
        _storePath = Path.Combine(
            Path.GetTempPath(),
            $"release-counters-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ResolveGeneration_returns_zero_for_first_codegen_version_in_empty_store()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);
    }

    [Fact]
    public void ResolveGeneration_holds_ordinal_steady_when_same_codegen_version_reresolved()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);
        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);
    }

    [Fact]
    public void ResolveGeneration_increments_ordinal_when_codegen_version_changes()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);
        store.ResolveGeneration("0.2.0-preview.4").Should().Be(1);
        store.ResolveGeneration("0.3.0").Should().Be(2);
        store.ResolveGeneration("0.2.0-preview.4").Should().Be(1);
    }

    [Fact]
    public void ResolveGeneration_seeds_high_water_from_legacy_revision_entries()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"x\", \"revision\": 2 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(3);
    }

    [Fact]
    public void ResolveGeneration_seeds_high_water_from_the_highest_legacy_revision_across_entries()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Daml.Finance.Account@1.0.0\": { \"content_hash\": \"a\", \"revision\": 0 }, " +
            "\"Daml.Finance.Holding@2.0.0\": { \"content_hash\": \"b\", \"revision\": 1 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(2);
    }

    [Fact]
    public void ResolveGeneration_persists_across_open_or_create_reopens()
    {
        JsonReleaseCounterStore.OpenOrCreate(_storePath).ResolveGeneration("0.2.0-preview.3").Should().Be(0);
        JsonReleaseCounterStore.OpenOrCreate(_storePath).ResolveGeneration("0.2.0-preview.4").Should().Be(1);

        JsonReleaseCounterStore.OpenOrCreate(_storePath).ResolveGeneration("0.2.0-preview.3").Should().Be(0);
        JsonReleaseCounterStore.OpenOrCreate(_storePath).ResolveGeneration("0.2.0-preview.4").Should().Be(1);
    }

    [Fact]
    public void ResolveGeneration_drops_legacy_entries_after_the_first_mint_rewrites_the_store()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"x\", \"revision\": 2 } }");

        JsonReleaseCounterStore.OpenOrCreate(_storePath).ResolveGeneration("0.2.0-preview.3").Should().Be(3);

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().HaveCount(2)
            .And.Contain("store_version")
            .And.Contain("codegen_generations");
    }

    [Fact]
    public void ReleaseCounterStore_persist_writes_codegen_generations_map_in_snake_case()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var generations = document.RootElement.GetProperty("codegen_generations");
        generations.GetProperty("0.2.0-preview.3").GetInt32().Should().Be(0);
    }

    [Fact]
    public void ReleaseCounterStore_persist_writes_a_store_version_marker_matching_the_current_schema_version()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        document.RootElement.GetProperty("store_version").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ReleaseCounterStore_persist_does_not_leave_a_dot_tmp_sibling_after_a_successful_write()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);

        File.Exists(_storePath).Should().BeTrue();
        File.Exists(_storePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_naming_the_path_when_file_contains_malformed_json()
    {
        File.WriteAllText(_storePath, "{ this is not valid json");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain(_storePath);
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_generation_ordinal_is_negative()
    {
        File.WriteAllText(_storePath, "{ \"codegen_generations\": { \"0.2.0-preview.3\": -5 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("0.2.0-preview.3");
    }

    [Fact]
    public void ResolveGeneration_seeds_high_water_across_both_new_generations_and_legacy_entries()
    {
        File.WriteAllText(
            _storePath,
            "{ \"store_version\": 1, \"codegen_generations\": { \"0.2.0-preview.3\": 1 }, " +
            "\"Splice.Amulet@0.1.17\": { \"content_hash\": \"x\", \"revision\": 4 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(1);
        store.ResolveGeneration("0.9.9").Should().Be(5);
    }

    [Fact]
    public void OpenOrCreate_resolves_a_new_shape_store_with_the_correct_store_version()
    {
        File.WriteAllText(
            _storePath,
            "{ \"store_version\": 1, \"codegen_generations\": { \"0.2.0-preview.3\": 4 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(4);
        store.ResolveGeneration("0.9.9").Should().Be(5);
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_codegen_generations_is_present_without_a_store_version_marker()
    {
        File.WriteAllText(
            _storePath,
            "{ \"codegen_generations\": { \"0.2.0-preview.3\": 4 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("store_version");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_the_store_version_is_unrecognized()
    {
        File.WriteAllText(
            _storePath,
            "{ \"store_version\": 99, \"codegen_generations\": { \"0.2.0-preview.3\": 4 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("store_version");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_the_store_version_is_not_numeric()
    {
        File.WriteAllText(
            _storePath,
            "{ \"store_version\": \"1\", \"codegen_generations\": { \"0.2.0-preview.3\": 4 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("store_version");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_store_version_marker_is_present_without_codegen_generations()
    {
        File.WriteAllText(_storePath, "{ \"store_version\": 1 }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("codegen_generations");
    }

    [Fact]
    public void OpenOrCreate_does_not_require_a_store_version_marker_for_a_legacy_only_store()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"x\", \"revision\": 2 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void OpenOrCreate_throws_invalid_data_exception_when_the_file_is_empty_or_whitespace(string blankContent)
    {
        File.WriteAllText(_storePath, blankContent);

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain(_storePath);
    }

    [Fact]
    public void OpenOrCreate_resolves_an_empty_json_object_as_a_bootstrap_empty_store()
    {
        File.WriteAllText(_storePath, "{}");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("0.2.0-preview.3").Should().Be(0);
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_legacy_entry_value_is_null()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": null }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_legacy_entry_content_hash_is_null()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": null, \"revision\": 1 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_legacy_entry_content_hash_is_missing()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": { \"revision\": 1 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_invalid_data_exception_when_a_legacy_entry_revision_is_negative()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"abc\", \"revision\": -5 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_rather_than_minting_zero_when_all_legacy_entries_are_malformed()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": null, \"Splice.Util@0.1.5\": { \"revision\": -3 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>();
    }
}
