// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.ContractKeys;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// A contract id is compared by record equality, which includes the runtime type, so any
/// second id type per template makes "is this the contract I just created?" silently false.
/// These scan the corpus's generated assembly for every contract-id-typed member and pin
/// that each one is the runtime <c>ContractId&lt;T&gt;</c> itself.
/// </summary>
public class ContractIdIdentityTests
{
    private const BindingFlags PublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] GeneratedTypes = typeof(Account).Assembly.GetExportedTypes();

    [Fact]
    public void ContractId_is_never_derived_by_a_generated_type()
    {
        GeneratedTypes.Where(type => typeof(ContractId).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .Should().BeEmpty("the runtime ContractId<T> is the only contract-id type a template has");
    }

    [Fact]
    public void ContractId_typed_generated_members_are_the_runtime_contract_id()
    {
        var contractIdTypedMembers = GeneratedTypes.SelectMany(ContractIdTypedMembers).ToList();

        contractIdTypedMembers.Should().NotBeEmpty("the corpus exercises choices that take and return contract ids");
        contractIdTypedMembers.Where(member => !IsRuntimeContractId(member.Type))
            .Select(member => $"{member.Name}: {member.Type}")
            .Should().BeEmpty("every generated accessor, parameter and return must hand out ContractId<T> itself");
    }

    [Fact]
    public void ContractId_of_a_created_event_equals_the_runtime_contract_id_holding_the_same_value()
    {
        var custodian = new Party("custodian::1220");
        var created = new CreatedEvent(
            EventId: "event-1",
            ContractId: "00abc",
            TemplateId: Account.TemplateId,
            CreateArguments: new Account(custodian, "savings", 42).ToRecord(),
            WitnessParties: [],
            Signatories: [custodian],
            Observers: [],
            ContractKey: new ContractKey(new AccountKey(custodian, "savings").ToRecord()));

        var contract = Contract<Account, AccountKey>.FromCreatedEvent(created, Account.FromRecord);
        var runtime = new ContractId<Account>("00abc");

        contract.Id.Should().Be(runtime);
        new HashSet<ContractId<Account>> { runtime }.Should().Contain(contract.Id);
    }

    private static bool IsRuntimeContractId(Type type) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(ContractId<>);

    private static IEnumerable<(string Name, Type Type)> ContractIdTypedMembers(Type declaringType)
    {
        var members = declaringType.GetProperties(PublicMembers)
            .Select(property => ($"{declaringType.Name}.{property.Name}", property.PropertyType))
            .Concat(declaringType.GetFields(PublicMembers)
                .Select(field => ($"{declaringType.Name}.{field.Name}", field.FieldType)))
            .Concat(declaringType.GetMethods(PublicMembers)
                .SelectMany(method => method.GetParameters()
                    .Select(parameter => ($"{declaringType.Name}.{method.Name}({parameter.Name})", parameter.ParameterType))
                    .Append(($"{declaringType.Name}.{method.Name}()", method.ReturnType))))
            .Concat(declaringType.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()
                    .Select(parameter => ($"{declaringType.Name}..ctor({parameter.Name})", parameter.ParameterType))));

        return members.SelectMany(member => ContractIdTypesWithin(member.Item2).Select(type => (member.Item1, type)));
    }

    private static IEnumerable<Type> ContractIdTypesWithin(Type type)
    {
        if (typeof(ContractId).IsAssignableFrom(type))
        {
            return [type];
        }

        return type.IsGenericType
            ? type.GetGenericArguments().SelectMany(ContractIdTypesWithin)
            : [];
    }
}
