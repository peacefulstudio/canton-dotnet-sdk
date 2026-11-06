// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;
using Grpc.Core.Interceptors;
using Xunit;

namespace Canton.Ledger.Grpc.Client.Tests;

public class MidStreamUnavailableInterceptorTests
{
    private static readonly string[] ThreeResponses = ["a", "b", "c"];

    [Fact]
    public async Task Passes_the_responses_before_the_cut_then_fails_with_Unavailable()
    {
        var call = Intercept(new MidStreamUnavailableInterceptor("GetActiveContracts", responsesKept: 2), "GetActiveContracts");

        var read = await ReadUntilFaultAsync(call.ResponseStream);

        Assert.Equal(["a", "b"], read.Responses);
        Assert.Equal(StatusCode.Unavailable, Assert.IsType<RpcException>(read.Fault).StatusCode);
    }

    [Fact]
    public async Task Fails_on_the_first_read_when_no_response_is_kept()
    {
        var call = Intercept(new MidStreamUnavailableInterceptor("GetActiveContracts", responsesKept: 0), "GetActiveContracts");

        var read = await ReadUntilFaultAsync(call.ResponseStream);

        Assert.Empty(read.Responses);
        Assert.Equal(StatusCode.Unavailable, Assert.IsType<RpcException>(read.Fault).StatusCode);
    }

    [Fact]
    public async Task Fails_when_the_stream_ends_before_the_cut()
    {
        var call = Intercept(new MidStreamUnavailableInterceptor("GetActiveContracts", responsesKept: 5), "GetActiveContracts");

        var read = await ReadUntilFaultAsync(call.ResponseStream);

        Assert.Equal(["a", "b", "c"], read.Responses);
        Assert.Equal(StatusCode.Unavailable, Assert.IsType<RpcException>(read.Fault).StatusCode);
    }

    [Fact]
    public async Task Leaves_a_stream_of_any_other_method_whole()
    {
        var call = Intercept(new MidStreamUnavailableInterceptor("GetActiveContracts", responsesKept: 0), "GetUpdates");

        var read = await ReadUntilFaultAsync(call.ResponseStream);

        Assert.Equal(["a", "b", "c"], read.Responses);
        Assert.Null(read.Fault);
    }

    private static AsyncServerStreamingCall<string> Intercept(MidStreamUnavailableInterceptor interceptor, string methodName)
    {
        var marshaller = Marshallers.Create(
            (string value) => System.Text.Encoding.UTF8.GetBytes(value),
            bytes => System.Text.Encoding.UTF8.GetString(bytes));
        var method = new Method<string, string>(
            MethodType.ServerStreaming, "com.daml.ledger.api.v2.StateService", methodName, marshaller, marshaller);
        var context = new ClientInterceptorContext<string, string>(method, host: null, new CallOptions());

        return interceptor.AsyncServerStreamingCall(
            "request",
            context,
            (_, _) => new AsyncServerStreamingCall<string>(
                new ListStreamReader(ThreeResponses),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => [],
                () => { }));
    }

    private static async Task<(List<string> Responses, Exception? Fault)> ReadUntilFaultAsync(IAsyncStreamReader<string> stream)
    {
        var responses = new List<string>();
        try
        {
            while (await stream.MoveNext(TestContext.Current.CancellationToken))
            {
                responses.Add(stream.Current);
            }

            return (responses, null);
        }
        catch (RpcException fault)
        {
            return (responses, fault);
        }
    }

    private sealed class ListStreamReader(IReadOnlyList<string> responses) : IAsyncStreamReader<string>
    {
        private int _next;

        public string Current { get; private set; } = string.Empty;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_next == responses.Count)
            {
                return Task.FromResult(false);
            }

            Current = responses[_next++];
            return Task.FromResult(true);
        }
    }
}
