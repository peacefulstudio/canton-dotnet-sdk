// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Grpc.Core;

namespace Canton.Ledger.Grpc.Client.Tests;

internal sealed class CannedResponseCallInvoker(Func<Type, IMessage> respond) : CallInvoker
{
    internal CannedResponseCallInvoker(WireResponseVariant variant)
        : this(variant.Build)
    {
    }

    internal static CannedResponseCallInvoker Answering(IMessage message) =>
        new(responseType => responseType == message.GetType() ? message : (IMessage)Activator.CreateInstance(responseType)!);

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        new(
            Task.FromResult((TResponse)respond(typeof(TResponse))),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException("the sweep only drives unary calls through the asynchronous path");

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
        throw new NotSupportedException("the sweep drives no streaming member");

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException("the sweep drives no streaming member");

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method, string? host, CallOptions options) =>
        throw new NotSupportedException("the sweep drives no streaming member");
}
