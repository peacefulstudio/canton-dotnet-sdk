// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class GoogleProtobufFileDescriptorCacheRegressionTests
{
    // Guards Directory.Packages.props' Google.Protobuf pin. 3.36.0-3.36.2 build this static,
    // non-thread-safe field the first time two threads race to initialize a *Reflection type
    // (https://github.com/protocolbuffers/protobuf/issues/29696), so this test fails whenever
    // the field is present. It reds if the pin regresses onto one of those versions, and stays
    // green past it once an upstream release drops the field for a thread-safe fix.
    [Fact]
    public void FileDescriptor_has_no_unlocked_static_extensions_cache_field()
    {
        var extensionsCacheField = typeof(FileDescriptor).GetField(
            "allDependedExtensionsCache", BindingFlags.NonPublic | BindingFlags.Static);

        extensionsCacheField.Should().BeNull();
    }
}
