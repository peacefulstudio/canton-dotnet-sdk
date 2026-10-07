// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class TransactionResultTests
{
    [Fact]
    public void Single_returns_contract_id_when_exactly_one_match()
    {
        var result = MakeTransaction(("00alice", FooBar.TemplateId));

        var id = result.Single<FooBar>();

        id.Value.Should().Be("00alice");
    }

    [Fact]
    public void Single_throws_when_no_matching_contract()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        Action act = () => result.Single<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*no contracts of type FooBar*");
    }

    [Fact]
    public void Single_throws_when_multiple_matching_contracts()
    {
        var result = MakeTransaction(
            ("00a", FooBar.TemplateId),
            ("00b", FooBar.TemplateId));

        Action act = () => result.Single<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2 contracts*FooBar*expected exactly 1*");
    }

    [Fact]
    public void TrySingle_returns_null_when_no_matching_contract()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        var id = result.TrySingle<FooBar>();

        id.Should().BeNull();
    }

    [Fact]
    public void TrySingle_returns_contract_id_when_exactly_one_match()
    {
        var result = MakeTransaction(("00alice", FooBar.TemplateId));

        var id = result.TrySingle<FooBar>();

        id.Should().NotBeNull();
        id!.Value.Should().Be("00alice");
    }

    [Fact]
    public void TrySingle_throws_when_multiple_matching_contracts()
    {
        var result = MakeTransaction(
            ("00a", FooBar.TemplateId),
            ("00b", FooBar.TemplateId));

        Action act = () => result.TrySingle<FooBar>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*2 contracts*FooBar*expected at most 1*");
    }

    [Fact]
    public void All_returns_empty_when_no_matching_contracts()
    {
        var result = MakeTransaction(("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")));

        var ids = result.All<FooBar>();

        ids.Should().BeEmpty();
    }

    [Fact]
    public void All_returns_contract_ids_in_transaction_order()
    {
        var result = MakeTransaction(
            ("00first", FooBar.TemplateId),
            ("00other", new RuntimeIdentifier("pkg", "Other", "Tpl")),
            ("00second", FooBar.TemplateId));

        var ids = result.All<FooBar>();

        ids.Should().HaveCount(2);
        ids[0].Value.Should().Be("00first");
        ids[1].Value.Should().Be("00second");
    }

    [Fact]
    public void All_returns_the_same_value_for_two_transactions_over_the_same_created_contracts()
    {
        var first = MakeTransaction(("00first", FooBar.TemplateId), ("00second", FooBar.TemplateId));
        var second = MakeTransaction(("00first", FooBar.TemplateId), ("00second", FooBar.TemplateId));

        first.All<FooBar>().Equals(second.All<FooBar>()).Should().BeTrue();
    }

    [Fact]
    public void All_returns_a_different_value_when_a_contract_id_differs()
    {
        var first = MakeTransaction(("00first", FooBar.TemplateId), ("00second", FooBar.TemplateId));
        var second = MakeTransaction(("00first", FooBar.TemplateId), ("00third", FooBar.TemplateId));

        first.All<FooBar>().Equals(second.All<FooBar>()).Should().BeFalse();
    }

    [Fact]
    public void Match_helpers_ignore_package_id_difference()
    {
        // Same module/entity but a different package id (e.g. upgrade) should still match —
        // template upgrades change the package hash but keep the qualified name stable.
        var differentPackage = new RuntimeIdentifier("pkg-v2", FooBar.TemplateId.ModuleName, FooBar.TemplateId.EntityName);
        var result = MakeTransaction(("00upgraded", differentPackage));

        result.Single<FooBar>().Value.Should().Be("00upgraded");
    }

    [Fact]
    public void CommandId_round_trips_on_the_transaction_result()
    {
        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: new CommandId("cmd-42"));

        result.CommandId?.Value.Should().Be("cmd-42");
    }

    [Fact]
    public void CommandId_is_absent_and_readable_when_the_participant_recorded_none()
    {
        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: null);

        var readCommandIdValue = () => result.CommandId?.Value;

        readCommandIdValue.Should().NotThrow(
            "an absent command id must be readable rather than detonate on first access");
        readCommandIdValue().Should().BeNull();
        result.CommandId.Should().BeNull();
    }

    [Fact]
    public void ExercisedEvents_defaults_to_empty_when_not_set()
    {
        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: null);

        result.ExercisedEvents.Should().NotBeNull();
        result.ExercisedEvents.Should().BeEmpty();
    }

    [Fact]
    public void ExercisedEvents_round_trips_when_initialized()
    {
        var exerciseResult = new DamlNumeric(42.5m);
        var argument = new DamlText("ping");
        var exercised = new ExercisedEvent(
            ContractId: "00alice",
            TemplateId: FooBar.TemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName("GetTrailingTwap"),
            ChoiceArgument: argument,
            ExerciseResult: exerciseResult,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice"), new Party("bob")]);

        var result = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: [],
            CommandId: null)
        {
            ExercisedEvents = [exercised],
        };

        result.ExercisedEvents.Should().HaveCount(1);
        result.ExercisedEvents[0].ChoiceName.Should().Be(new ChoiceName("GetTrailingTwap"));
        result.ExercisedEvents[0].ExerciseResult.Should().BeSameAs(exerciseResult);
        result.ExercisedEvents[0].ChoiceArgument.Should().BeSameAs(argument);
        result.ExercisedEvents[0].Consuming.Should().BeFalse();
        result.ExercisedEvents[0].InterfaceId.Should().BeNull();
        result.ExercisedEvents[0].ActingParties.Should().ContainSingle().Which.Value.Should().Be("alice");
    }

    [Fact]
    public void ExercisedEvents_with_expression_preserves_other_fields()
    {
        var original = new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(5),
            CreatedContracts: [MakeCreated("00a")],
            ArchivedContractIds: ["00b"],
            CommandId: null);

        var exercised = new ExercisedEvent(
            ContractId: "00c",
            TemplateId: FooBar.TemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName("DoThing"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")]);

        var withEvents = original with { ExercisedEvents = [exercised] };

        withEvents.UpdateId.Should().Be("u1");
        withEvents.CompletionOffset.Should().Be(LedgerOffset.At(5));
        withEvents.CreatedContracts.Should().HaveCount(1);
        withEvents.ArchivedContractIds.Should().ContainSingle().Which.Should().Be("00b");
        withEvents.ExercisedEvents.Should().ContainSingle();
        // Original is unmodified.
        original.ExercisedEvents.Should().BeEmpty();
    }

    [Fact]
    public void ExercisedEvent_supports_inherited_interface_choice()
    {
        var interfaceId = new RuntimeIdentifier("pkg", "Acme.Iface", "IThing");
        var exercised = new ExercisedEvent(
            ContractId: "00a",
            TemplateId: FooBar.TemplateId,
            InterfaceId: interfaceId,
            ChoiceName: new ChoiceName("Inherited"),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: false,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")]);

        exercised.InterfaceId.Should().Be(interfaceId);
        exercised.TemplateId.Should().NotBe(interfaceId);
    }

    [Fact]
    public void CreatedContract_InterfaceIds_defaults_to_empty_when_not_set()
    {
        var contract = MakeCreated("00alice");

        contract.InterfaceIds.Should().NotBeNull();
        contract.InterfaceIds.Should().BeEmpty();
    }

    [Fact]
    public void CreatedContract_InterfaceIds_round_trips_a_single_interface_id()
    {
        var holding = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.HoldingV1", "Holding");

        var contract = MakeCreated("00alice") with { InterfaceIds = [holding] };

        contract.InterfaceIds.Should().ContainSingle().Which.Should().Be(holding);
    }

    [Fact]
    public void CreatedContract_InterfaceIds_round_trips_multiple_interface_ids()
    {
        var holding = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.HoldingV1", "Holding");
        var factory = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.AllocationFactoryV1", "AllocationFactory");

        var contract = MakeCreated("00alice") with { InterfaceIds = [holding, factory] };

        contract.InterfaceIds.Should().HaveCount(2);
        contract.InterfaceIds[0].Should().Be(holding);
        contract.InterfaceIds[1].Should().Be(factory);
    }

    [Fact]
    public void CreatedContract_equality_compares_structurally_equal_payloads_as_equal()
    {
        var first = MakeCreated("00alice", payload: DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice"))));
        var second = MakeCreated("00alice", payload: DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice"))));

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void CreatedContract_equality_compares_structurally_equal_interface_ids_as_equal()
    {
        var holding = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.HoldingV1", "Holding");
        var first = MakeCreated("00alice") with { InterfaceIds = [holding] };
        var second = MakeCreated("00alice") with { InterfaceIds = [holding] };

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_interface_ids()
    {
        var contract = MakeCreated("00alice");
        var withInterface = contract with
        {
            InterfaceIds = [new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.HoldingV1", "Holding")],
        };

        contract.Should().NotBe(withInterface);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_contract_ids()
    {
        var first = MakeCreated("00alice");
        var second = first with { ContractId = "00bob" };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_template_ids()
    {
        var first = MakeCreated("00alice");
        var second = MakeCreated("00alice", templateId: new RuntimeIdentifier("other-pkg", "Other.Mod", "Other"));

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_when_interface_id_order_differs()
    {
        var holding = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.HoldingV1", "Holding");
        var factory = new RuntimeIdentifier("splice-pkg", "Splice.Api.Token.AllocationFactoryV1", "AllocationFactory");
        var first = MakeCreated("00alice") with { InterfaceIds = [holding, factory] };
        var second = MakeCreated("00alice") with { InterfaceIds = [factory, holding] };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_payloads()
    {
        var first = MakeCreated("00alice", payload: DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice"))));
        var second = MakeCreated("00alice", payload: DamlRecord.Create(DamlField.Create("owner", new DamlParty("bob"))));

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_deconstructs_into_every_mirrored_field()
    {
        var payload = DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")));
        var key = new ContractKey(payload, FooBar.TemplateId);
        var createdAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);
        var contract = MakeCreated("00alice", payload: payload) with
        {
            ContractKey = key,
            CreatedAt = createdAt,
        };

        var (eventId, contractId, templateId, deconstructed, witnesses, signatories, observers, contractKey, at) =
            contract;

        eventId.Should().Be("evt-00alice");
        contractId.Should().Be("00alice");
        templateId.Should().Be(FooBar.TemplateId);
        deconstructed.Should().BeSameAs(payload);
        witnesses.Should().ContainSingle().Which.Value.Should().Be("alice");
        signatories.Should().ContainSingle().Which.Value.Should().Be("alice");
        observers.Should().BeEmpty();
        contractKey.Should().Be(key);
        at.Should().Be(createdAt);
    }

    [Fact]
    public void CreatedContract_mirrors_TreeEvent_Created_parameter_for_parameter()
    {
        var mirror = typeof(CreatedContract).GetConstructors().Single().GetParameters();
        var source = typeof(TreeEvent.Created).GetConstructors().Single().GetParameters();

        mirror.Select(p => p.ParameterType).Should().Equal(source.Select(p => p.ParameterType));
        mirror.Select(p => p.HasDefaultValue).Should().Equal(source.Select(p => p.HasDefaultValue));

        const string TreeSideCreateArgumentsName = "CreateArguments";
        const string FlattenedSideCreateArgumentsName = "Payload";
        var expectedNamesWithTheOneRenamedSlot = source.Select(p =>
            p.Name == TreeSideCreateArgumentsName ? FlattenedSideCreateArgumentsName : p.Name);

        mirror.Select(p => p.Name).Should().Equal(expectedNamesWithTheOneRenamedSlot);
    }

    [Fact]
    public void CreatedContract_mirrors_TreeEvent_Created_in_its_init_only_slots_too()
    {
        var mirror = InitOnlySlotsOutsideTheConstructor(typeof(CreatedContract));
        var source = InitOnlySlotsOutsideTheConstructor(typeof(TreeEvent.Created))
            .Select(slot => slot.Name == "UndecodedCreateArguments" ? ("UndecodedPayload", slot.Type) : slot)
            .OrderBy(slot => slot.Item1, StringComparer.Ordinal)
            .ToList();

        mirror.Should().NotBeEmpty(
            "both sides emptying out would make the comparison below pass vacuously");
        mirror.Should().Equal(source);
    }

    /// <summary>
    /// The init-only half of the mirror. The constructor comparison above cannot see these
    /// slots, and <see cref="TransactionTreeExtensions.ToTransactionResult"/> only fails to
    /// compile for a slot it actually forwards — so a new init-only member on one side alone
    /// would leave both green while falsifying the field-for-field claim in the doc comments.
    /// </summary>
    private static IReadOnlyList<(string Name, Type Type)> InitOnlySlotsOutsideTheConstructor(Type type)
    {
        var constructorParameterNames = type.GetConstructors().Single()
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToHashSet(StringComparer.Ordinal);

        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !constructorParameterNames.Contains(property.Name))
            .Where(IsInitOnly)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => (property.Name, property.PropertyType))
            .ToList();
    }

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod is { } setter
        && setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    [Fact]
    public void CreatedContract_leaves_key_and_created_at_absent_when_not_supplied()
    {
        var contract = MakeCreated("00alice");

        contract.ContractKey.Should().BeNull();
        contract.CreatedAt.Should().BeNull();
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_keys()
    {
        var first = MakeCreated("00alice") with
        {
            ContractKey = new ContractKey(DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")))),
        };
        var second = MakeCreated("00alice") with
        {
            ContractKey = new ContractKey(DamlRecord.Create(DamlField.Create("owner", new DamlParty("bob")))),
        };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_compares_structurally_equal_keys_as_equal()
    {
        var createdAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);
        var first = MakeCreated("00alice") with
        {
            ContractKey = new ContractKey(DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")))),
            CreatedAt = createdAt,
        };
        var second = MakeCreated("00alice") with
        {
            ContractKey = new ContractKey(DamlRecord.Create(DamlField.Create("owner", new DamlParty("alice")))),
            CreatedAt = createdAt,
        };

        first.ContractKey.Should().NotBeSameAs(second.ContractKey);
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_created_at()
    {
        var first = MakeCreated("00alice") with
        {
            CreatedAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero),
        };
        var second = MakeCreated("00alice") with
        {
            CreatedAt = new DateTimeOffset(2026, 8, 24, 9, 31, 0, TimeSpan.Zero),
        };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_event_ids()
    {
        var first = MakeCreated("00alice");
        var second = first with { EventId = "evt-other" };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_compares_stakeholder_lists_element_wise()
    {
        var first = MakeCreated("00alice") with
        {
            WitnessParties = [new("alice"), new("bob")],
            Signatories = [new("alice")],
            Observers = [new("bob")],
        };
        var second = MakeCreated("00alice") with
        {
            WitnessParties = [new("alice"), new("bob")],
            Signatories = [new("alice")],
            Observers = [new("bob")],
        };

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_signatories()
    {
        var first = MakeCreated("00alice") with { Signatories = [new Party("alice")] };
        var second = MakeCreated("00alice") with { Signatories = [new Party("bob")] };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_when_witness_order_differs()
    {
        var first = MakeCreated("00alice") with
        {
            WitnessParties = [new Party("alice"), new Party("bob")],
        };
        var second = MakeCreated("00alice") with
        {
            WitnessParties = [new Party("bob"), new Party("alice")],
        };

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreatedContract_equality_distinguishes_contracts_with_different_observers()
    {
        var first = MakeCreated("00alice") with { Observers = [] };
        var second = MakeCreated("00alice") with { Observers = [new Party("bob")] };

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_compares_created_contracts_element_wise()
    {
        var first = MakeTransaction(("00alice", FooBar.TemplateId));
        var second = MakeTransaction(("00alice", FooBar.TemplateId));

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_with_different_created_contracts()
    {
        var first = MakeTransaction(("00alice", FooBar.TemplateId));
        var second = MakeTransaction(("00bob", FooBar.TemplateId));

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_when_created_order_differs()
    {
        var first = MakeTransaction(("00alice", FooBar.TemplateId), ("00bob", FooBar.TemplateId));
        var second = MakeTransaction(("00bob", FooBar.TemplateId), ("00alice", FooBar.TemplateId));

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_compares_archived_contract_ids_element_wise()
    {
        var first = MakeResult(archived: ["00a", "00b"]);
        var second = MakeResult(archived: ["00a", "00b"]);

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_when_archived_order_differs()
    {
        var first = MakeResult(archived: ["00a", "00b"]);
        var second = MakeResult(archived: ["00b", "00a"]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_compares_ExercisedEvents_by_list_content_not_by_list_identity()
    {
        var first = MakeResult(exercised: [MakeExercised("DoThing")]);
        var second = MakeResult(exercised: [MakeExercised("DoThing")]);

        first.ExercisedEvents[0].Should().NotBeSameAs(second.ExercisedEvents[0]);
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_ExercisedEvents_lists_of_different_length()
    {
        var first = MakeResult(exercised: [MakeExercised("DoThing")]);
        var second = MakeResult(
            exercised: [MakeExercised("DoThing"), MakeExercised("DoThing")]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_when_exercised_order_differs()
    {
        var doThing = MakeExercised("DoThing");
        var doOther = MakeExercised("DoOther");
        var first = MakeResult(exercised: [doThing, doOther]);
        var second = MakeResult(exercised: [doOther, doThing]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_with_different_update_ids()
    {
        var result = MakeResult();

        (result with { UpdateId = "u2" }).Should().NotBe(result);
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_with_different_completion_offsets()
    {
        var result = MakeResult();

        (result with { CompletionOffset = LedgerOffset.At(99) }).Should().NotBe(result);
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_with_different_command_ids()
    {
        var result = MakeResult();

        (result with { CommandId = new CommandId("cmd-1") }).Should().NotBe(result);
        (result with { CommandId = new CommandId("cmd-1") })
            .Should().NotBe(result with { CommandId = new CommandId("cmd-2") });
    }

    [Fact]
    public void TransactionResult_equality_distinguishes_results_with_different_archived_contract_ids()
    {
        var first = MakeResult(archived: ["00a"]);
        var second = MakeResult(archived: ["00b"]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Single_matches_created_contract_via_interface_view()
    {
        var result = MakeTransactionWithInterfaces(
            ("00holding", new RuntimeIdentifier("impl-pkg", "Acme.Impl", "Concrete"), [IThing.InterfaceId]));

        var id = result.Single<IThing>();

        id.Value.Should().Be("00holding");
    }

    [Fact]
    public void Interface_match_ignores_package_id_and_template_id()
    {
        var differentPackageInterfaceId = new RuntimeIdentifier(
            "iface-pkg-v2", IThing.InterfaceId.ModuleName, IThing.InterfaceId.EntityName);
        var result = MakeTransactionWithInterfaces(
            ("00upgraded", FooBar.TemplateId, [differentPackageInterfaceId]));

        result.Single<IThing>().Value.Should().Be("00upgraded");
    }

    [Fact]
    public void Interface_match_excludes_contracts_without_the_interface_view()
    {
        var result = MakeTransactionWithInterfaces(
            ("00other", FooBar.TemplateId, [new RuntimeIdentifier("p", "Other.Iface", "IOther")]),
            ("00thing", FooBar.TemplateId, [IThing.InterfaceId]));

        var ids = result.All<IThing>();

        ids.Should().ContainSingle().Which.Value.Should().Be("00thing");
    }

    [Fact]
    public void Template_match_ignores_interface_views()
    {
        var result = MakeTransactionWithInterfaces(
            ("00thing", new RuntimeIdentifier("impl-pkg", "Acme.Impl", "Concrete"), [IThing.InterfaceId]));

        result.TrySingle<FooBar>().Should().BeNull();
    }

    [Fact]
    public void TrySingle_throws_when_two_contracts_match_by_interface_view()
    {
        var result = MakeTransactionWithInterfaces(
            ("00first", new RuntimeIdentifier("impl-pkg", "Acme.Impl", "Concrete"), [IThing.InterfaceId]),
            ("00second", FooBar.TemplateId, [IThing.InterfaceId]));

        Action act = () => result.TrySingle<IThing>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreatedContract_stays_findable_in_a_set_after_the_producer_mutates_its_list()
    {
        var witnesses = new List<Party> { new("alice") };
        var created = MakeCreated("00alice") with { WitnessParties = EquatableArray.Create(witnesses) };
        var set = new HashSet<CreatedContract> { created };

        witnesses.Add(new Party("mallory"));

        set.Should().Contain(created);
    }

    private static CreatedContract MakeCreated(
        string contractId,
        RuntimeIdentifier? templateId = null,
        DamlRecord? payload = null) =>
        new(
            EventId: $"evt-{contractId}",
            ContractId: contractId,
            TemplateId: templateId ?? FooBar.TemplateId,
            Payload: payload ?? DamlRecord.Create(),
            WitnessParties: [new Party("alice")],
            Signatories: [new Party("alice")],
            Observers: []);

    private static TransactionResult MakeResult(
        EquatableArray<string> archived = default,
        EquatableArray<ExercisedEvent> exercised = default) =>
        new(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [],
            ArchivedContractIds: archived,
            CommandId: null)
        {
            ExercisedEvents = exercised,
        };

    private static ExercisedEvent MakeExercised(string choiceName) =>
        new(
            ContractId: "00c",
            TemplateId: FooBar.TemplateId,
            InterfaceId: null,
            ChoiceName: new ChoiceName(choiceName),
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: [new Party("alice")],
            WitnessParties: [new Party("alice")]);

    private static TransactionResult MakeTransaction(params (string ContractId, RuntimeIdentifier TemplateId)[] created)
    {
        var contracts = new List<CreatedContract>();
        foreach (var (cid, tid) in created)
        {
            contracts.Add(MakeCreated(cid, tid));
        }
        return new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [.. contracts],
            ArchivedContractIds: [],
            CommandId: null);
    }

    private static TransactionResult MakeTransactionWithInterfaces(
        params (string ContractId, RuntimeIdentifier TemplateId, RuntimeIdentifier[] InterfaceIds)[] created)
    {
        var contracts = new List<CreatedContract>();
        foreach (var (cid, tid, interfaceIds) in created)
        {
            contracts.Add(MakeCreated(cid, tid) with { InterfaceIds = [.. interfaceIds] });
        }
        return new TransactionResult(
            UpdateId: "u1",
            CompletionOffset: LedgerOffset.At(1),
            CreatedContracts: [.. contracts],
            ArchivedContractIds: [],
            CommandId: null);
    }

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

    private sealed record IThing(string Owner) : IDamlInterface
    {
        public static RuntimeIdentifier InterfaceId { get; } = new("iface-pkg", "Acme.Iface", "IThing");
        public static string PackageId => "iface-pkg";
        public static string PackageName => "iface-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(InterfaceId, DamlTypeKind.Interface, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}
