// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Streams;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class ReassignmentPairingFieldTypingTests
{
    private static readonly SynchronizerId Source = new("global_sync::abc::35-0");
    private static readonly SynchronizerId Target = new("local_sync::def::35-0");
    private static readonly Party Alice = new("alice");

    [Fact]
    public void Unassigned_should_carry_ReassignmentId_and_ReassignmentCounter()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Unassigned(
            new ContractId<TestTemplate>("c1"),
            LedgerOffset.At(5),
            Source,
            Target,
            "reassignment-42",
            9L,
            [Alice]);

        ev.ReassignmentId.Should().Be("reassignment-42");
        ev.ReassignmentCounter.Should().Be(9L);
    }

    [Fact]
    public void Assigned_should_carry_ReassignmentId_and_ReassignmentCounter()
    {
        var ev = new ContractStreamEvent<TestTemplate>.Assigned(
            new ContractId<TestTemplate>("c1"),
            new TestTemplate("alice"),
            null,
            LedgerOffset.At(6),
            Source,
            Target,
            "reassignment-42",
            9L,
            [Alice]);

        ev.ReassignmentId.Should().Be("reassignment-42");
        ev.ReassignmentCounter.Should().Be(9L);
    }

    [Theory]
    [InlineData(typeof(ContractStreamEvent<TestTemplate>.Assigned))]
    [InlineData(typeof(ContractStreamEvent<TestTemplate>.Unassigned))]
    public void Reassignment_fields_are_string_and_long_positioned_between_Target_and_WitnessParties(Type variant)
    {
        var parameters = variant.GetConstructors()
            .OrderByDescending(constructor => constructor.GetParameters().Length)
            .First()
            .GetParameters()
            .ToDictionary(parameter => parameter.Name!);

        parameters["ReassignmentId"].ParameterType.Should().Be<string>();
        parameters["ReassignmentCounter"].ParameterType.Should().Be<long>();

        parameters["ReassignmentId"].Position.Should().Be(parameters["Target"].Position + 1);
        parameters["ReassignmentCounter"].Position.Should().Be(parameters["ReassignmentId"].Position + 1);
        parameters["WitnessParties"].Position.Should().Be(parameters["ReassignmentCounter"].Position + 1);
    }
}
