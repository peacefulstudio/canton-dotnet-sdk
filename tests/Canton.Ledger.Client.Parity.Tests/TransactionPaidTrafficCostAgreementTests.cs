// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Xunit;
using ProtoTransaction = Com.Daml.Ledger.Api.V2.Transaction;
using WireTransaction = Canton.Ledger.Rest.Client.Raw.Transaction;

namespace Canton.Ledger.Client.Parity.Tests;

/// <summary>
/// Pins that one transaction reaches a caller with the same <c>PaidTrafficCost</c> on
/// <c>TransactionResult</c> and on <c>TransactionTree</c> whichever transport carried it.
/// <c>transaction.proto</c> declares <c>paid_traffic_cost</c> as <c>optional</c>, so a cost the
/// participant never reported is <c>null</c>, and a reported zero stays zero. Each row holds both
/// wire encodings of one transaction: the protobuf message and the JSON Ledger API DTO. Both
/// projectors are <c>internal</c> and stay that way; this project reaches them through
/// <c>InternalsVisibleTo</c> from each client.
/// </summary>
public class TransactionPaidTrafficCostAgreementTests
{
    public static TheoryData<string> PayloadNames() => [.. Payloads.Keys];

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_report_the_same_paid_traffic_cost_on_a_transaction_result(string payloadName)
    {
        var payload = Payloads[payloadName];

        GrpcTransactionResultProjector.Project(payload.OverGrpc())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
        RestTransactionResultProjector.Project(payload.OverRest())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
    }

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_report_the_same_paid_traffic_cost_on_an_acs_delta_transaction_result(string payloadName)
    {
        var payload = Payloads[payloadName];

        GrpcTransactionResultProjector.ProjectAcsDelta(payload.OverGrpc())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
        RestTransactionResultProjector.ProjectAcsDelta(payload.OverRest())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
    }

    [Theory]
    [MemberData(nameof(PayloadNames))]
    public void Both_transports_report_the_same_paid_traffic_cost_on_a_transaction_tree(string payloadName)
    {
        var payload = Payloads[payloadName];

        GrpcTransactionTreeProjector.Project(payload.OverGrpc())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
        RestTransactionTreeProjector.Project(payload.OverRest())
            .PaidTrafficCost.Should().Be(payload.PaidTrafficCost);
    }

    private static readonly IReadOnlyDictionary<string, TransactionPayload> Payloads =
        new Dictionary<string, TransactionPayload>(StringComparer.Ordinal)
        {
            ["a transaction carrying a paid traffic cost"] = new(
                () => Proto(proto => proto.PaidTrafficCost = 4096L),
                () => Wire(wire => wire.PaidTrafficCost = "4096"),
                PaidTrafficCost: 4096L),

            ["a transaction carrying a paid traffic cost of zero"] = new(
                () => Proto(proto => proto.PaidTrafficCost = 0L),
                () => Wire(wire => wire.PaidTrafficCost = "0"),
                PaidTrafficCost: 0L),

            ["a transaction carrying no paid traffic cost"] = new(
                () => Proto(_ => { }),
                () => Wire(_ => { }),
                PaidTrafficCost: null),

            ["a transaction whose JSON paid traffic cost is empty"] = new(
                () => Proto(_ => { }),
                () => Wire(wire => wire.PaidTrafficCost = string.Empty),
                PaidTrafficCost: null),
        };

    private static ProtoTransaction Proto(Action<ProtoTransaction> configure)
    {
        var proto = new ProtoTransaction { UpdateId = "update-1", Offset = 17L };
        configure(proto);
        return proto;
    }

    private static WireTransaction Wire(Action<WireTransaction> configure)
    {
        var wire = new WireTransaction { UpdateId = "update-1", Offset = "17" };
        configure(wire);
        return wire;
    }

    private sealed record TransactionPayload(
        Func<ProtoTransaction> OverGrpc,
        Func<WireTransaction> OverRest,
        long? PaidTrafficCost);
}
