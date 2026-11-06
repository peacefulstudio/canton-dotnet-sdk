// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Kernel.Wire;
using Com.Daml.Ledger.Api.V2.Interactive;
using Daml.Runtime.Commands;
using Google.Protobuf;
using Xunit;

namespace Canton.Ledger.Kernel.Tests.Wire;

public class PreparedTransactionCommandIdTests
{
    private static PreparedTransaction Naming(string commandId) =>
        new() { Metadata = new Metadata { SubmitterInfo = new Metadata.Types.SubmitterInfo { CommandId = commandId } } };

    [Fact]
    public void Of_reads_the_command_id_from_the_submitter_info_of_the_metadata()
    {
        PreparedTransactionCommandId.Of(Naming("cmd-7")).Should().Be(new CommandId("cmd-7"));
    }

    [Fact]
    public void Of_reads_the_command_id_from_the_serialized_prepared_transaction()
    {
        PreparedTransactionCommandId.Of(Naming("cmd-8").ToByteArray()).Should().Be(new CommandId("cmd-8"));
    }

    [Fact]
    public void Of_returns_null_when_the_metadata_names_no_command_id()
    {
        PreparedTransactionCommandId.Of(new PreparedTransaction()).Should().BeNull();
    }

    [Fact]
    public void Of_returns_null_for_bytes_that_are_not_a_prepared_transaction()
    {
        PreparedTransactionCommandId.Of(new byte[] { 0x0A, 0x03 }).Should().BeNull();
    }
}
