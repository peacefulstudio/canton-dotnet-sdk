// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class ExerciseOutcomeTests
{
    [Fact]
    public void One_carries_contract_id_payload()
    {
        var cid = new ContractId<FooBar>("00abc");
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.One(cid);

        outcome.Result.Should().BeSameAs(cid);
    }

    [Fact]
    public void One_carries_transaction_result_payload()
    {
        var result = new TransactionResult("u1", LedgerOffset.At(1), [], [], null);
        var outcome = new ExerciseOutcome<TransactionResult>.One(result);

        outcome.Result.Should().BeSameAs(result);
    }

    [Fact]
    public void One_can_carry_a_decimal_payload()
    {
        // The outcome type imposes no constraint on T — choice results that aren't
        // template-typed (e.g. a Decimal returned from a `nonconsuming` choice) round-trip.
        var outcome = new ExerciseOutcome<decimal>.One(42.5m);

        outcome.Result.Should().Be(42.5m);
    }

    [Fact]
    public void One_can_carry_an_arbitrary_record_payload()
    {
        // Composite Daml choice results are emitted as plain C# records by codegen;
        // the outcome wrapper accepts any T.
        var result = new SwapChoiceResult(new ContractId<FooBar>("00agreement"), new ContractId<FooBar>("00record"));
        var outcome = new ExerciseOutcome<SwapChoiceResult>.One(result);

        outcome.Result.Should().Be(result);
    }

    [Fact]
    public void None_constructs_without_state()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.None();

        outcome.Should().NotBeNull();
    }

    [Fact]
    public void Many_derives_its_count_from_the_contract_ids_it_carries()
    {
        var ids = new[] { "00a", "00b", "00c" };
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(EquatableArray.Create(ids));

        outcome.Count.Should().Be(3);
        outcome.ContractIds.Should().Equal(ids);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Many_refuses_the_candidate_counts_the_None_and_One_arms_are_for(int candidateCount)
    {
        var candidates = Enumerable.Range(0, candidateCount).Select(index => $"00{index}").ToArray();

        var construct = () => new ExerciseOutcome<ContractId<FooBar>>.Many(EquatableArray.Create(candidates));

        construct.Should().Throw<ArgumentException>()
            .WithMessage($"*at least two*got {candidateCount}*")
            .And.ParamName.Should().Be("ContractIds");
    }

    [Fact]
    public void Many_refuses_a_with_expression_that_drops_it_below_two_contract_ids()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);

        var narrow = () => outcome with { ContractIds = ["00a"] };

        narrow.Should().Throw<ArgumentException>()
            .WithMessage("*at least two*got 1*")
            .And.ParamName.Should().Be("ContractIds");
    }

    [Fact]
    public void Many_takes_a_with_expression_that_widens_its_contract_ids()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);

        var widened = outcome with { ContractIds = ["00a", "00b", "00c"] };

        widened.ContractIds.Should().Equal("00a", "00b", "00c");
        widened.Count.Should().Be(3);
        widened.Should().Be(new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b", "00c"]));
    }

    [Fact]
    public void Many_deconstructs_to_the_single_contract_ids_member()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);

        outcome.Deconstruct(out var contractIds);

        contractIds.Should().Equal("00a", "00b");
    }

    [Fact]
    public void Many_compares_equal_when_the_same_contract_ids_arrive_in_separate_sequences()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);
        var rebuilt = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);

        outcome.Should().Be(rebuilt);
        outcome.GetHashCode().Should().Be(rebuilt.GetHashCode());
    }

    [Fact]
    public void Many_keeps_its_contract_ids_when_the_producer_mutates_the_list_it_kept()
    {
        var ids = new List<string> { "00a", "00b" };
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(EquatableArray.Create(ids));

        ids[0] = "00z";
        ids.Add("00c");

        outcome.ContractIds.Should().Equal("00a", "00b");
    }

    [Fact]
    public void Many_compares_unequal_when_a_contract_id_differs()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00b"]);
        var other = new ExerciseOutcome<ContractId<FooBar>>.Many(["00a", "00c"]);

        outcome.Should().NotBe(other);
    }

    [Fact]
    public void DamlError_carries_full_payload()
    {
        var metadata = new Dictionary<string, string> { ["category"] = "InvalidGivenCurrentSystemStateOther" };
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "ACME_SWAP_ALREADY_EXECUTED",
            "swap already executed",
            metadata);

        outcome.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        outcome.ErrorId.Should().Be("ACME_SWAP_ALREADY_EXECUTED");
        outcome.Message.Should().Be("swap already executed");
        outcome.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void DamlError_works_for_transaction_result_payload()
    {
        var metadata = new Dictionary<string, string> { ["category"] = "ContentionOnSharedResources" };
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "CONTRACT_NOT_FOUND",
            "contract not found",
            metadata);

        outcome.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        outcome.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        outcome.Message.Should().Be("contract not found");
        outcome.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void DamlError_compares_equal_when_the_same_metadata_arrives_in_separate_dictionaries()
    {
        var outcome = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00abc" });
        var rebuilt = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00abc" });

        outcome.Should().Be(rebuilt);
        outcome.GetHashCode().Should().Be(rebuilt.GetHashCode());
    }

    [Fact]
    public void DamlError_compares_equal_when_the_same_metadata_was_built_in_a_different_order()
    {
        var outcome = ContractNotFoundWith(
            new Dictionary<string, string> { ["cid"] = "00abc", ["retryable"] = "false" });
        var rebuilt = ContractNotFoundWith(
            new Dictionary<string, string> { ["retryable"] = "false", ["cid"] = "00abc" });

        outcome.Should().Be(rebuilt);
        outcome.GetHashCode().Should().Be(rebuilt.GetHashCode());
    }

    [Fact]
    public void DamlError_compares_unequal_when_a_metadata_value_differs()
    {
        var outcome = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00abc" });
        var other = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00zzz" });

        outcome.Should().NotBe(other);
    }

    [Fact]
    public void DamlError_compares_unequal_when_the_other_carries_metadata_it_does_not()
    {
        var outcome = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00abc" });
        var other = ContractNotFoundWith(
            new Dictionary<string, string> { ["cid"] = "00abc", ["retryable"] = "false" });

        outcome.Should().NotBe(other);
    }

    [Fact]
    public void DamlError_keeps_its_metadata_when_the_producer_mutates_the_dictionary_it_kept()
    {
        var metadata = new Dictionary<string, string> { ["cid"] = "00abc" };
        var outcome = ContractNotFoundWith(metadata);
        var hashBeforeTheProducerMutatedItsDictionary = outcome.GetHashCode();

        metadata["cid"] = "00zzz";
        metadata["retryable"] = "false";

        outcome.Metadata.Should().ContainSingle();
        outcome.Metadata["cid"].Should().Be("00abc");
        outcome.GetHashCode().Should().Be(hashBeforeTheProducerMutatedItsDictionary);
    }

    [Fact]
    public void DamlError_keeps_the_metadata_a_with_expression_supplied_once_the_producer_mutates_it()
    {
        var replacement = new Dictionary<string, string> { ["cid"] = "00abc" };
        var outcome = ContractNotFoundWith(new Dictionary<string, string>()) with { Metadata = replacement };

        replacement["cid"] = "00zzz";

        outcome.Metadata["cid"].Should().Be("00abc");
    }

    [Fact]
    public void DamlError_compares_unequal_when_the_metadata_keys_are_disjoint()
    {
        var outcome = ContractNotFoundWith(new Dictionary<string, string> { ["cid"] = "00abc" });
        var other = ContractNotFoundWith(new Dictionary<string, string> { ["offset"] = "00abc" });

        outcome.Should().NotBe(other);
    }

    [Theory]
    [InlineData(DamlErrorCategory.Unknown, ContractNotFoundId, ContractNotFoundMessage)]
    [InlineData(ContractNotFoundCategory, "CONTRACT_KEY_NOT_FOUND", ContractNotFoundMessage)]
    [InlineData(ContractNotFoundCategory, ContractNotFoundId, "contract was archived")]
    public void DamlError_compares_unequal_when_a_member_beside_the_metadata_differs(
        DamlErrorCategory category,
        string errorId,
        string message)
    {
        var outcome = ContractNotFoundWith(EmptyMetadata);
        var other = ContractNotFoundWith(EmptyMetadata, category, errorId, message);

        outcome.Should().NotBe(other);
    }

    [Fact]
    public void DamlError_compares_unequal_to_another_outcome_variant_and_to_null()
    {
        ExerciseOutcome<ContractId<FooBar>> outcome = ContractNotFoundWith(EmptyMetadata);
        ExerciseOutcome<ContractId<FooBar>> infra =
            new ExerciseOutcome<ContractId<FooBar>>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "network down");

        outcome.Should().NotBe(infra);
        infra.Should().NotBe(outcome);
        outcome.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void DamlError_rejects_null_metadata_at_the_producer()
    {
        Action act = () => _ = ContractNotFoundWith(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("Metadata");
    }

    [Fact]
    public void DamlError_rejects_null_metadata_supplied_by_a_with_expression()
    {
        Action act = () => _ = ContractNotFoundWith(EmptyMetadata) with { Metadata = null! };

        act.Should().Throw<ArgumentNullException>().WithParameterName("Metadata");
    }

    [Fact]
    public void InfraError_carries_status_message_and_source_exception()
    {
        var sourceException = new InvalidOperationException("transport failed");
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded), "deadline", SourceException: sourceException);

        outcome.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.DeadlineExceeded));
        outcome.Message.Should().Be("deadline");
        outcome.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void InfraError_leaves_the_category_null_when_it_is_not_supplied()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "network down");

        outcome.Category.Should().BeNull();
    }

    [Fact]
    public void InfraError_carries_a_category_determined_without_a_structured_error()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(
            new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied), "permission denied", DamlErrorCategory.AuthorizationChecksFailed);

        outcome.Category.Should().Be(DamlErrorCategory.AuthorizationChecksFailed);
        outcome.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.PermissionDenied));
        outcome.SourceException.Should().BeNull();
    }

    [Fact]
    public void InfraError_works_for_transaction_result_payload()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "network down");

        outcome.Status.Should().Be(new TransportStatus.Grpc(GrpcStatusCode.Unavailable));
        outcome.Message.Should().Be("network down");
    }

    [Fact]
    public void CommittedUndecodable_carries_update_id_message_and_source_exception()
    {
        var sourceException = new InvalidOperationException("decode failed");
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable(
            "u1", "could not decode result", sourceException);

        outcome.UpdateId.Should().Be("u1");
        outcome.Message.Should().Be("could not decode result");
        outcome.SourceException.Should().BeSameAs(sourceException);
    }

    [Fact]
    public void CommittedUndecodable_leaves_the_update_id_null_when_the_decode_failure_precedes_it()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable(
            null, "could not decode result", new InvalidOperationException());

        outcome.UpdateId.Should().BeNull();
    }

    [Fact]
    public void CommittedUndecodable_compares_unequal_to_another_outcome_variant_and_to_null()
    {
        ExerciseOutcome<ContractId<FooBar>> outcome = new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable(
            "u1", "could not decode result", new InvalidOperationException());
        ExerciseOutcome<ContractId<FooBar>> infra =
            new ExerciseOutcome<ContractId<FooBar>>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "network down");

        outcome.Should().NotBe(infra);
        infra.Should().NotBe(outcome);
        outcome.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void CommittedUndecodable_compares_equal_to_another_CommittedUndecodable_with_the_same_values()
    {
        var sourceException = new InvalidOperationException("decode failed");
        ExerciseOutcome<ContractId<FooBar>> first = new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable(
            "u1", "could not decode result", sourceException);
        ExerciseOutcome<ContractId<FooBar>> second = new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable(
            "u1", "could not decode result", sourceException);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Variants_should_be_distinguishable_via_pattern_match()
    {
        ExerciseOutcome<ContractId<FooBar>>[] outcomes =
        [
            new ExerciseOutcome<ContractId<FooBar>>.One(new ContractId<FooBar>("c")),
            new ExerciseOutcome<ContractId<FooBar>>.None(),
            new ExerciseOutcome<ContractId<FooBar>>.Many(["c1", "c2"]),
            new ExerciseOutcome<ContractId<FooBar>>.DamlError(DamlErrorCategory.Unknown, "X", "x", new Dictionary<string, string>()),
            new ExerciseOutcome<ContractId<FooBar>>.InfraError(new TransportStatus.Grpc(GrpcStatusCode.Unavailable), "u"),
            new ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable("u1", "bad", new InvalidOperationException()),
        ];

        var seen = outcomes.Select(o => o switch
        {
            ExerciseOutcome<ContractId<FooBar>>.One => "one",
            ExerciseOutcome<ContractId<FooBar>>.None => "none",
            ExerciseOutcome<ContractId<FooBar>>.Many => "many",
            ExerciseOutcome<ContractId<FooBar>>.DamlError => "daml-err",
            ExerciseOutcome<ContractId<FooBar>>.InfraError => "infra-err",
            ExerciseOutcome<ContractId<FooBar>>.CommittedUndecodable => "committed-undecodable",
            _ => "other",
        }).ToList();

        seen.Should().Equal("one", "none", "many", "daml-err", "infra-err", "committed-undecodable");
    }

    private const DamlErrorCategory ContractNotFoundCategory = DamlErrorCategory.ContentionOnSharedResources;
    private const string ContractNotFoundId = "CONTRACT_NOT_FOUND";
    private const string ContractNotFoundMessage = "contract not found";

    private static IReadOnlyDictionary<string, string> EmptyMetadata => new Dictionary<string, string>();

    private static ExerciseOutcome<ContractId<FooBar>>.DamlError ContractNotFoundWith(
        IReadOnlyDictionary<string, string> metadata,
        DamlErrorCategory category = ContractNotFoundCategory,
        string errorId = ContractNotFoundId,
        string message = ContractNotFoundMessage) => new(category, errorId, message, metadata);

    private sealed record SwapChoiceResult(
        ContractId<FooBar> Agreement,
        ContractId<FooBar> SwapRecord);

    private sealed record FooBar(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Acme.Foo", "FooBar");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
