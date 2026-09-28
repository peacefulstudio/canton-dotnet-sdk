// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace Daml.Runtime.Tests;

public class GrpcStatusCodeTests
{
    [Fact]
    public void GrpcStatusCode_numbers_each_member_as_the_gRPC_wire_status()
    {
        Enum.GetValues<GrpcStatusCode>().ToDictionary(code => code.ToString(), code => (int)code)
            .Should().Equal(new Dictionary<string, int>
            {
                ["OK"] = 0,
                ["Cancelled"] = 1,
                ["Unknown"] = 2,
                ["InvalidArgument"] = 3,
                ["DeadlineExceeded"] = 4,
                ["NotFound"] = 5,
                ["AlreadyExists"] = 6,
                ["PermissionDenied"] = 7,
                ["ResourceExhausted"] = 8,
                ["FailedPrecondition"] = 9,
                ["Aborted"] = 10,
                ["OutOfRange"] = 11,
                ["Unimplemented"] = 12,
                ["Internal"] = 13,
                ["Unavailable"] = 14,
                ["DataLoss"] = 15,
                ["Unauthenticated"] = 16,
            });
    }
}
