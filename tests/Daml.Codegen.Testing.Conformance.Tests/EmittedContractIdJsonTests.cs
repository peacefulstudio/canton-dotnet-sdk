// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Contracts;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Every generated contract-id member hands out the runtime <c>ContractId&lt;T&gt;</c>, whose own
/// <c>[JsonConverter]</c> attribute fixes the wire shape. These pin that a consumer DTO holding a
/// corpus template's contract id reads and writes a bare JSON string with the default
/// <see cref="JsonSerializerOptions"/>, without registering the Daml converters.
/// </summary>
public class EmittedContractIdJsonTests
{
    private sealed record AccountReference(ContractId<Account> Id);

    [Fact]
    public void ContractId_of_a_generated_template_serializes_as_a_bare_string_without_AddDamlConverters()
    {
        JsonSerializer.Serialize(new ContractId<Account>("00abc")).Should().Be("\"00abc\"");
    }

    [Fact]
    public void ContractId_of_a_generated_template_deserializes_from_a_bare_string_without_AddDamlConverters()
    {
        JsonSerializer.Deserialize<ContractId<Account>>("\"00abc\"").Should().Be(new ContractId<Account>("00abc"));
    }

    [Fact]
    public void ContractId_of_a_generated_template_round_trips_inside_a_dto_without_AddDamlConverters()
    {
        const string json = "{\"Id\":\"00abc\"}";

        var reference = JsonSerializer.Deserialize<AccountReference>(json);

        reference!.Id.Should().Be(new ContractId<Account>("00abc"));
        JsonSerializer.Serialize(reference).Should().Be(json);
    }
}
