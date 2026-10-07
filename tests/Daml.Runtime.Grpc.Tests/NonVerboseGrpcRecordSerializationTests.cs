// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Serialization;
using Xunit;
using ProtoRecord = Com.Daml.Ledger.Api.V2.Record;
using ProtoRecordField = Com.Daml.Ledger.Api.V2.RecordField;
using ProtoValue = Com.Daml.Ledger.Api.V2.Value;

namespace Daml.Runtime.Grpc.Tests;

public class NonVerboseGrpcRecordSerializationTests
{
    [Fact]
    public void Serialize_should_throw_for_a_one_field_record_read_over_non_verbose_grpc()
    {
        var nonVerboseRecord = new ProtoRecord();
        nonVerboseRecord.Fields.Add(new ProtoRecordField { Value = new ProtoValue { Text = "hello" } });
        var record = DamlValueConverter.FromProtoRecord(nonVerboseRecord);

        var act = () => DamlJsonSerializer.Serialize(record);

        act.Should().Throw<JsonException>().WithMessage("A Daml record field label must not be empty");
    }
}
