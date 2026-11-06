// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
    private static LedgerOperation SampleOperation => new LedgerOperation.Command(SampleIdentifier, "Accept");

    private static void AddOutcomes(Dictionary<Type, object[]> samples)
    {
        samples[typeof(DamlErrorCategory)] = [DamlErrorCategory.ContentionOnSharedResources];
        samples[typeof(GrpcStatusCode)] = [GrpcStatusCode.Unavailable];
        samples[typeof(TransportStatus)] =
        [
            new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
            new TransportStatus.Http(HttpStatusCode.ServiceUnavailable),
            new TransportStatus.NoResponse(),
            new TransportStatus.UndecodableBody(),
        ];
        samples[typeof(ByKeyExercise<SampleTemplate, string>)] =
        [
            new ByKeyExercise<SampleTemplate, string>(new ContractId<SampleTemplate>("00c0ffee"), "result"),
        ];
        samples[typeof(LedgerOperation)] =
        [
            SampleOperation,
            new LedgerOperation.Batch([new(SampleIdentifier, "Accept"), new(SampleIdentifier, "Create")]),
        ];
        samples[typeof(ExerciseOutcome<string>)] =
        [
            new ExerciseOutcome<string>.One("result") { CommandId = "cmd-1", Operation = SampleOperation },
            new ExerciseOutcome<string>.None { CommandId = "cmd-1", Operation = SampleOperation },
            new ExerciseOutcome<string>.Many(Eq("00aa", "00bb")) { CommandId = "cmd-1", Operation = SampleOperation },
            new ExerciseOutcome<string>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "contract 00aa is gone",
                new Dictionary<string, string> { ["contract"] = "00aa", ["category"] = "11" })
            {
                CommandId = "cmd-1",
                Operation = SampleOperation,
            },
            new ExerciseOutcome<string>.InfraError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                "node unreachable",
                DamlErrorCategory.TransientServerFailure,
                SampleSourceException)
            {
                CommandId = "cmd-1",
                Operation = SampleOperation,
            },
            new ExerciseOutcome<string>.CommittedUndecodable("update-1", "result did not decode", SampleSourceException)
            {
                CommandId = "cmd-1",
                Operation = SampleOperation,
            },
        ];
    }
}
