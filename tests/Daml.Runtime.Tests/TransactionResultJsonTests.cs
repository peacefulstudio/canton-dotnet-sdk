// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Holds <see cref="TransactionResult"/> to the round trip a caller who persists one and
/// reloads it depends on: the same result back, with the offset it resumes from and the
/// command id it deduplicates on intact.
/// </summary>
public class TransactionResultJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions { Converters = { new DamlValueJsonConverter() } }.AddDamlConverters();

    private static readonly Party Alice = new("Alice::122012ab");

    private static readonly CreatedContract AssetContract = new(
        "event-1",
        "contract-1",
        new Identifier("cafe", "Asset", "Token"),
        new DamlRecord(null, [new DamlField("amount", new DamlInt64(42))]),
        [Alice],
        [Alice],
        [],
        null,
        null);

    private static readonly TransactionResult Result = new(
        "update-1",
        LedgerOffset.At(6057),
        [AssetContract],
        ["contract-2"],
        new CommandId("cmd-1"));

    [Fact]
    public void TransactionResult_round_trips_through_json()
    {
        var restored = JsonSerializer.Deserialize<TransactionResult>(
            JsonSerializer.Serialize(Result, Options),
            Options);

        restored.Should().Be(Result);
    }

    [Fact]
    public void TransactionResult_writes_the_offset_as_a_number_and_the_command_id_as_a_string()
    {
        var json = JsonSerializer.Serialize(Result, Options);

        json.Should().Contain("\"CompletionOffset\":6057").And.Contain("\"CommandId\":\"cmd-1\"");
    }

    [Fact]
    public void TransactionResult_round_trips_the_command_id_the_participant_omitted()
    {
        var unattributed = Result with { CommandId = null };

        var restored = JsonSerializer.Deserialize<TransactionResult>(
            JsonSerializer.Serialize(unattributed, Options),
            Options);

        restored.Should().Be(unattributed);
    }

    [Fact]
    public void ExercisedEvents_ChoiceName_serializes_as_the_same_bare_JSON_string_a_plain_string_field_would()
    {
        var withExercise = Result with
        {
            ExercisedEvents = EquatableArray.Create(
            [
                new ExercisedEvent(
                    "contract-1",
                    new Identifier("cafe", "Asset", "Token"),
                    null,
                    new ChoiceName("Transfer"),
                    new DamlText("ping"),
                    new DamlNumeric(42.5m),
                    true,
                    [Alice],
                    [Alice]),
            ]),
        };

        var json = JsonSerializer.Serialize(withExercise, Options);

        json.Should().Contain("\"ChoiceName\":\"Transfer\"");

        var restored = JsonSerializer.Deserialize<TransactionResult>(json, Options);

        restored.Should().Be(withExercise);
    }
}
