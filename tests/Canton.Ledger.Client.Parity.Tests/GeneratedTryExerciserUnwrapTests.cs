// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins the throwing style a caller of a generated <c>Try…Async</c> exerciser gets by chaining
/// <c>OneOrThrowAsync</c>: the codegen emits no throwing counterpart, so this chain is the only
/// way back to a plain result or a <see cref="LedgerOperationException"/>.
/// </summary>
public class GeneratedTryExerciserUnwrapTests
{
    private static readonly Party Alice = new("party::alice");

    [Fact]
    public async Task OneOrThrowAsync_unwraps_a_generated_TryCreateAsync_into_the_created_contract_id()
    {
        ILedgerWriter client = new FakeLedgerClientBuilder()
            .WithCreateResult(LedgerOutcomes.One(new ContractId<Marker>("00marker")))
            .Build();

        var contractId = await client
            .TryCreateAsync(new Marker(Alice), TestContext.Current.CancellationToken)
            .OneOrThrowAsync("Create Marker");

        contractId.Should().Be(new ContractId<Marker>("00marker"));
    }

    [Fact]
    public async Task OneOrThrowAsync_throws_LedgerOperationException_for_a_generated_TryCreateAsync_Daml_error()
    {
        ILedgerWriter client = new FakeLedgerClientBuilder()
            .WithCreateResult(LedgerOutcomes.DamlError<ContractId<Marker>>(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "contract not found",
                new Dictionary<string, string>()))
            .Build();

        var act = async () => await client
            .TryCreateAsync(new Marker(Alice), TestContext.Current.CancellationToken)
            .OneOrThrowAsync("Create Marker");

        var thrown = await act.Should().ThrowAsync<LedgerOperationException>();
        thrown.Which.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing);
        thrown.Which.Operation.Should().Be("Create Marker");
    }
}
