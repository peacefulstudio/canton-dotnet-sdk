// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

const int UnrecognizedInvocation = 64;

if (args.Length > 0 && string.Equals(args[0], "-version", StringComparison.Ordinal))
{
    Console.Error.WriteLine("fake-java version \"17.0.0\"");
    return 0;
}

Console.Error.WriteLine($"fake-java: unrecognized invocation: {string.Join(' ', args)}");
return UnrecognizedInvocation;
