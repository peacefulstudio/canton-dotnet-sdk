// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

public class DamlLfJsonReaderRepeatedDecodeTests
{
    public sealed record ScoredProfile(
        [property: DamlFieldAttribute("nickname")] string Nickname,
        [property: DamlFieldAttribute("score")] long Score) : IDamlRecord<ScoredProfile>
    {
        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("nickname", new DamlText(Nickname)),
            DamlField.Create("score", new DamlInt64(Score)));

        public static ScoredProfile FromRecord(DamlRecord record) => new(
            record.GetRequiredField("nickname").As<DamlText>().Value,
            record.GetRequiredField("score").As<DamlInt64>().Value);

        public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
        {
            DamlLfJsonDecoders.RequireObject(json, context);
            return DamlRecord.Create(
                DamlField.Create("nickname", DamlLfJsonDecoders.ReadText(
                    DamlLfJsonDecoders.RequireField(json, context, "nickname"), context.Field("nickname"))),
                DamlField.Create("score", DamlLfJsonDecoders.ReadInt64(
                    DamlLfJsonDecoders.RequireField(json, context, "score"), context.Field("score"))));
        }
    }

    [Fact]
    public void ReadRecord_should_order_fields_by_declaration_rather_than_by_json_property_order()
    {
        var record = DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"score":"7","nickname":"nick"}""");

        record.Should().Be(new ScoredProfile("nick", 7L).ToRecord());
    }

    [Fact]
    public void ReadRecord_should_still_refuse_a_missing_field_once_the_type_has_been_decoded_before()
    {
        DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"nickname":"nick","score":"7"}""");

        var act = () => DamlLfJsonReader.ReadRecord<ScoredProfile>("""{"nickname":"nick"}""");

        act.Should().Throw<JsonException>()
            .WithMessage("Required Daml field 'ScoredProfile.score' is missing from the JSON object");
    }
}
