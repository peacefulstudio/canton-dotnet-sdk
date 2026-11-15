// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Canton.Ledger.AspNetCore;

internal sealed class LedgerOperationExceptionMiddleware(RequestDelegate next, LedgerOperationExceptionHandler handler)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (LedgerOperationException failure) when (!context.Response.HasStarted)
        {
            context.Response.Clear();
            await handler.TryHandleAsync(context, failure, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
