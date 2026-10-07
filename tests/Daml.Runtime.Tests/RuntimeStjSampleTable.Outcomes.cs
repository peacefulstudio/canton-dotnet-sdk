// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace Daml.Runtime.Tests;

internal static partial class RuntimeStjSampleTable
{
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
        samples[typeof(ExerciseOutcome<string>)] =
        [
            new ExerciseOutcome<string>.One("result"),
            new ExerciseOutcome<string>.None(),
            new ExerciseOutcome<string>.Many(Eq("00aa", "00bb")),
            new ExerciseOutcome<string>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
                "CONTRACT_NOT_FOUND",
                "contract 00aa is gone",
                new Dictionary<string, string> { ["contract"] = "00aa", ["category"] = "11" }),
            new ExerciseOutcome<string>.InfraError(
                new TransportStatus.Grpc(GrpcStatusCode.Unavailable),
                "node unreachable",
                DamlErrorCategory.TransientServerFailure,
                SampleSourceException),
            new ExerciseOutcome<string>.CommittedUndecodable("update-1", "result did not decode", SampleSourceException),
        ];
    }
}
