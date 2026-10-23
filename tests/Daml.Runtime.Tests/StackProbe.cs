// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Tests;

internal static class StackProbe
{
    public static Exception? OnStackOf(int kilobytes, Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            },
            maxStackSize: kilobytes * 1024);
        thread.Start();
        thread.Join();
        return thrown;
    }
}
