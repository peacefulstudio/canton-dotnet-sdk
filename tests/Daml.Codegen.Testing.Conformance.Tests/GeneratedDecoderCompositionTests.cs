// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Daml.Runtime.Stdlib;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Hand-composes <see cref="DamlLfJsonDecoders"/> entry points exactly as an emitted
/// choice-result or contract-key decoder would, then feeds the decoded
/// <see cref="DamlValue"/> into the real corpus <c>ResultDecoder</c>/<c>KeyDecoder</c>
/// delegates — proving the composable reader API produces values the corpus's own
/// generated conversion logic accepts, for every richtypes generic-result choice and
/// contract-key shape in the corpus.
/// </summary>
public class GeneratedDecoderCompositionTests
{
    [Fact]
    public void ReturnContractIds_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse(
            """["00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899","0011112222333344445555666677778888999900001111222233334444555566aa"]""");
        var context = DamlLfJsonDecodeContext.Root("ReturnContractIds");

        var decoded = DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadContractId);
        var result = GenericResults.ChoiceReturnContractIds.ResultDecoder(decoded);

        result.Should().BeEquivalentTo(
            new List<ContractId<GenericResults>>
            {
                new("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
                new("0011112222333344445555666677778888999900001111222233334444555566aa"),
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void ReturnEither_composed_decoder_matches_the_generated_ResultDecoder_for_Left()
    {
        using var document = JsonDocument.Parse("""{"tag":"Left","value":"boom"}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnEither");

        var decoded = DamlLfJsonDecoders.ReadEither(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnEither.ResultDecoder(decoded);

        result.Should().Be(new Either<string, long>.Left("boom"));
    }

    [Fact]
    public void ReturnEither_composed_decoder_matches_the_generated_ResultDecoder_for_Right()
    {
        using var document = JsonDocument.Parse("""{"tag":"Right","value":"7"}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnEither");

        var decoded = DamlLfJsonDecoders.ReadEither(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnEither.ResultDecoder(decoded);

        result.Should().Be(new Either<string, long>.Right(7));
    }

    [Fact]
    public void ReturnGenMap_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse("""[["gold","1"],["silver","2"]]""");
        var context = DamlLfJsonDecodeContext.Root("ReturnGenMap");

        var decoded = DamlLfJsonDecoders.ReadGenMap(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnGenMap.ResultDecoder(decoded);

        result.Should().BeEquivalentTo(new Dictionary<string, long> { ["gold"] = 1, ["silver"] = 2 });
    }

    [Fact]
    public void ReturnNestedOptional_composed_decoder_matches_the_generated_ResultDecoder_for_Some_of_Some()
    {
        using var document = JsonDocument.Parse("""[["ink"]]""");
        var context = DamlLfJsonDecodeContext.Root("ReturnNestedOptional");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));
        var result = GenericResults.ChoiceReturnNestedOptional.ResultDecoder(decoded);

        result.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.Some("ink")));
    }

    [Fact]
    public void ReturnNestedOptional_composed_decoder_matches_the_generated_ResultDecoder_for_Some_of_None()
    {
        using var document = JsonDocument.Parse("""[[]]""");
        var context = DamlLfJsonDecodeContext.Root("ReturnNestedOptional");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));
        var result = GenericResults.ChoiceReturnNestedOptional.ResultDecoder(decoded);

        result.Should().Be(new Optional<Optional<string>>.Some(new Optional<string>.None()));
    }

    [Fact]
    public void ReturnNestedOptional_composed_decoder_matches_the_generated_ResultDecoder_for_None()
    {
        using var document = JsonDocument.Parse("""[]""");
        var context = DamlLfJsonDecodeContext.Root("ReturnNestedOptional");

        var decoded = DamlLfJsonDecoders.ReadOptionalChain(
            document.RootElement,
            context,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptionalChain(element, elementContext, DamlLfJsonDecoders.ReadText));
        var result = GenericResults.ChoiceReturnNestedOptional.ResultDecoder(decoded);

        result.Should().Be(new Optional<Optional<string>>.None());
    }

    [Fact]
    public void ReturnNonEmpty_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse("""{"hd":"1","tl":["2","3"]}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnNonEmpty");

        var decoded = DamlLfJsonDecoders.ReadNonEmpty(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnNonEmpty.ResultDecoder(decoded);

        result.Should().Be(new NonEmpty<long>(1, [2, 3]));
    }

    [Fact]
    public void ReturnOptionalText_composed_decoder_matches_the_generated_ResultDecoder_for_Some()
    {
        using var document = JsonDocument.Parse("\"hello\"");
        var context = DamlLfJsonDecodeContext.Root("ReturnOptionalText");

        var decoded = DamlLfJsonDecoders.ReadOptional(document.RootElement, context, DamlLfJsonDecoders.ReadText);
        var result = GenericResults.ChoiceReturnOptionalText.ResultDecoder(decoded);

        result.Should().Be("hello");
    }

    [Fact]
    public void ReturnOptionalText_composed_decoder_matches_the_generated_ResultDecoder_for_None()
    {
        using var document = JsonDocument.Parse("null");
        var context = DamlLfJsonDecodeContext.Root("ReturnOptionalText");

        var decoded = DamlLfJsonDecoders.ReadOptional(document.RootElement, context, DamlLfJsonDecoders.ReadText);
        var result = GenericResults.ChoiceReturnOptionalText.ResultDecoder(decoded);

        result.Should().BeNull();
    }

    [Fact]
    public void ReturnSet_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse("""{"map":[["1",{}],["2",{}]]}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnSet");

        var decoded = DamlLfJsonDecoders.ReadSet(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnSet.ResultDecoder(decoded);

        result.Should().Be(new Set<long>([1, 2]));
    }

    [Fact]
    public void ReturnTextMap_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse("""{"gold":"1","silver":"2"}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnTextMap");

        var decoded = DamlLfJsonDecoders.ReadTextMap(document.RootElement, context, DamlLfJsonDecoders.ReadInt64);
        var result = GenericResults.ChoiceReturnTextMap.ResultDecoder(decoded);

        result.Should().BeEquivalentTo(new Dictionary<string, long> { ["gold"] = 1, ["silver"] = 2 });
    }

    [Fact]
    public void ReturnTuple_composed_decoder_matches_the_generated_ResultDecoder()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42"}""");
        var context = DamlLfJsonDecodeContext.Root("ReturnTuple");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadText, null, DamlLfJsonDecoders.ReadInt64, null);
        var result = GenericResults.ChoiceReturnTuple.ResultDecoder(decoded);

        result.Should().Be(new Tuple2<string, long>("gold", 42));
    }

    [Fact]
    public void ReturnContractIds_ResultJsonDecoder_decodes_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse(
            """["00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899","0011112222333344445555666677778888999900001111222233334444555566aa"]""");

        var result = GenericResults.ChoiceReturnContractIds.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnContractIds"));

        result.Should().BeEquivalentTo(
            new List<ContractId<GenericResults>>
            {
                new("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
                new("0011112222333344445555666677778888999900001111222233334444555566aa"),
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void ReturnEither_ResultJsonDecoder_decodes_Left_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""{"tag":"Left","value":"boom"}""");

        var result = GenericResults.ChoiceReturnEither.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnEither"));

        result.Should().Be(new Either<string, long>.Left("boom"));
    }

    [Fact]
    public void ReturnEither_ResultJsonDecoder_decodes_Right_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""{"tag":"Right","value":"7"}""");

        var result = GenericResults.ChoiceReturnEither.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnEither"));

        result.Should().Be(new Either<string, long>.Right(7));
    }

    [Fact]
    public void ReturnGenMap_ResultJsonDecoder_decodes_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""[["gold","1"],["silver","2"]]""");

        var result = GenericResults.ChoiceReturnGenMap.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnGenMap"));

        result.Should().BeEquivalentTo(new Dictionary<string, long> { ["gold"] = 1, ["silver"] = 2 });
    }

    [Fact]
    public void ReturnOptionalText_ResultJsonDecoder_decodes_Some_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("\"hello\"");

        var result = GenericResults.ChoiceReturnOptionalText.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnOptionalText"));

        result.Should().Be("hello");
    }

    [Fact]
    public void ReturnOptionalText_ResultJsonDecoder_decodes_None_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("null");

        var result = GenericResults.ChoiceReturnOptionalText.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnOptionalText"));

        result.Should().BeNull();
    }

    [Fact]
    public void ReturnTuple_ResultJsonDecoder_decodes_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""{"_1":"gold","_2":"42"}""");

        var result = GenericResults.ChoiceReturnTuple.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("ReturnTuple"));

        result.Should().Be(new Tuple2<string, long>("gold", 42));
    }

    /// <summary>
    /// <c>Holding.Split</c> is an interface choice: per
    /// <see cref="IHoldingExtensions"/>, interface choices have no typed
    /// <c>&lt;Choice&gt;Result</c> projection because the implementing template is
    /// unknown at the call site, so unlike the <see cref="GenericResults"/> choices above
    /// there is no generated <c>ResultDecoder</c> to delegate to. This test hand-composes
    /// the decode down to the final typed result the same way the generated
    /// <see cref="IHolding.ChoiceSplit"/>'s own <c>ResultJsonDecoder</c> does, and asserts
    /// the two agree.
    /// </summary>
    [Fact]
    public void HoldingSplit_composed_decoder_produces_the_expected_contract_id_list()
    {
        using var document = JsonDocument.Parse(
            """["00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899","0011112222333344445555666677778888999900001111222233334444555566aa"]""");
        var context = DamlLfJsonDecodeContext.Root("Split");

        var decoded = DamlLfJsonDecoders.ReadList(document.RootElement, context, DamlLfJsonDecoders.ReadContractId);
        var result = decoded.As<DamlList>().Values
            .Select(value => new ContractId<IHolding>(value.As<DamlContractId>().Value))
            .ToList();

        var expected = new List<ContractId<IHolding>>
        {
            new("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"),
            new("0011112222333344445555666677778888999900001111222233334444555566aa"),
        };
        result.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());

        using var replay = JsonDocument.Parse(
            """["00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899","0011112222333344445555666677778888999900001111222233334444555566aa"]""");
        var generated = IHolding.ChoiceSplit.ResultJsonDecoder(replay.RootElement, DamlLfJsonDecodeContext.Root("Split"));
        generated.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void HoldingArchive_ResultJsonDecoder_decodes_unit_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("{}");

        var result = IHolding.ChoiceArchive.ResultJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void HoldingArchive_ArgumentJsonDecoder_decodes_unit_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("{}");

        var argument = IHolding.ChoiceArchive.ArgumentJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        argument.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void HoldingDescribe_ArgumentJsonDecoder_and_ResultJsonDecoder_decode_directly_from_the_generated_choice()
    {
        using var argumentDocument = JsonDocument.Parse("""{"prefix":"balance: "}""");
        using var resultDocument = JsonDocument.Parse("\"balance: 42\"");

        var argument = IHolding.ChoiceDescribe.ArgumentJsonDecoder(argumentDocument.RootElement, DamlLfJsonDecodeContext.Root("Describe"));
        var result = IHolding.ChoiceDescribe.ResultJsonDecoder(resultDocument.RootElement, DamlLfJsonDecodeContext.Root("Describe"));

        argument.Should().Be(new Describe("balance: "));
        result.Should().Be("balance: 42");
    }

    [Fact]
    public void HoldingReissue_ArgumentJsonDecoder_and_ResultJsonDecoder_decode_directly_from_the_generated_choice()
    {
        using var argumentDocument = JsonDocument.Parse("""{"newAmount":"12.5"}""");
        using var resultDocument = JsonDocument.Parse(
            "\"00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899\"");

        var argument = IHolding.ChoiceReissue.ArgumentJsonDecoder(argumentDocument.RootElement, DamlLfJsonDecodeContext.Root("Reissue"));
        var result = IHolding.ChoiceReissue.ResultJsonDecoder(resultDocument.RootElement, DamlLfJsonDecodeContext.Root("Reissue"));

        argument.Should().Be(new Reissue(12.5m));
        result.Should().Be(new ContractId<IHolding>("00aabbccddeeff00112233445566778899aabbccddeeff00112233445566778899"));
    }

    [Fact]
    public void HoldingSplit_ArgumentJsonDecoder_decodes_the_pieces_field_directly_from_the_generated_choice()
    {
        using var document = JsonDocument.Parse("""{"pieces":"3"}""");

        var argument = IHolding.ChoiceSplit.ArgumentJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Split"));

        argument.Should().Be(new Split(3));
    }

    [Fact]
    public void MembershipKey_composed_decoder_matches_the_generated_KeyDecoder()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice::1220ab","_2":"savings"}""");
        var context = DamlLfJsonDecodeContext.Root("Membership.Key");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement, context, DamlLfJsonDecoders.ReadParty, null, DamlLfJsonDecoders.ReadText, null);
        var result = Membership.Key.KeyDecoder(decoded);

        result.Should().Be(new Tuple2<Party, string>(new Party("alice::1220ab"), "savings"));
    }

    [Fact]
    public void EnrollmentKey_composed_decoder_matches_the_generated_KeyDecoder_with_a_present_optional_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice::1220ab","_2":"secondary"}""");
        var context = DamlLfJsonDecodeContext.Root("Enrollment.Key");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadParty,
            null,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText),
            DamlOptional.None);
        var result = Enrollment.Key.KeyDecoder(decoded);

        result.Should().Be(new Tuple2<Party, Optional<string>>(new Party("alice::1220ab"), new Optional<string>.Some("secondary")));
    }

    [Fact]
    public void EnrollmentKey_composed_decoder_matches_the_generated_KeyDecoder_with_an_absent_optional_component()
    {
        using var document = JsonDocument.Parse("""{"_1":"alice::1220ab","_2":null}""");
        var context = DamlLfJsonDecodeContext.Root("Enrollment.Key");

        var decoded = DamlLfJsonDecoders.ReadTuple2(
            document.RootElement,
            context,
            DamlLfJsonDecoders.ReadParty,
            null,
            (element, elementContext) => DamlLfJsonDecoders.ReadOptional(element, elementContext, DamlLfJsonDecoders.ReadText),
            DamlOptional.None);
        var result = Enrollment.Key.KeyDecoder(decoded);

        result.Should().Be(new Tuple2<Party, Optional<string>>(new Party("alice::1220ab"), new Optional<string>.None()));
    }
}
