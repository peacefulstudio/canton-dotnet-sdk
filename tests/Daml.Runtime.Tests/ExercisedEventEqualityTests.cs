// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins that <see cref="ExercisedEvent"/> compares by content rather than by the identity of
/// its collection members, which is what makes <see cref="TransactionResult"/> equality
/// structural all the way down rather than only at the list level.
/// </summary>
public class ExercisedEventEqualityTests
{
    [Fact]
    public void Two_independently_built_events_describing_the_same_exercise_are_equal()
    {
        var first = MakeExercised();
        var second = MakeExercised();

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Events_with_different_acting_parties_are_not_equal()
    {
        var first = MakeExercised(actingParties: [new Party("alice")]);
        var second = MakeExercised(actingParties: [new Party("bob")]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Events_with_acting_parties_in_a_different_order_are_not_equal()
    {
        var first = MakeExercised(actingParties: [new Party("alice"), new Party("bob")]);
        var second = MakeExercised(actingParties: [new Party("bob"), new Party("alice")]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Acting_and_witness_parties_are_hashed_with_a_length_separator()
    {
        var split = MakeExercised(
            actingParties: [new Party("alice")],
            witnessParties: [new Party("bob")]);
        var merged = MakeExercised(
            actingParties: [new Party("alice"), new Party("bob")],
            witnessParties: []);

        split.Should().NotBe(merged);
        split.GetHashCode().Should().NotBe(merged.GetHashCode());
    }

    private static ExercisedEvent MakeExercised(
        EquatableArray<Party>? actingParties = null,
        EquatableArray<Party>? witnessParties = null) =>
        new(
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            InterfaceId: null,
            ChoiceName: new ChoiceName("DoThing"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: actingParties ?? [new Party("alice")],
            WitnessParties: witnessParties ?? [new Party("alice")]);
}
