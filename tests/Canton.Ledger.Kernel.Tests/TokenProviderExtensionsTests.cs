// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Kernel.Authentication;
using AwesomeAssertions;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class TokenProviderExtensionsTests
{
    [Fact]
    public void TokenProviderExtensions_is_internal() =>
        typeof(TokenProviderExtensions).IsPublic.Should().BeFalse();

    [Fact]
    public async Task ResolveBearerTokenAsync_returns_null_for_ITokenProvider_None()
    {
        var token = await ITokenProvider.None.ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_returns_null_for_null_provider()
    {
        ITokenProvider? provider = null;

        var token = await provider.ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeNull();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_returns_the_token_from_the_provider()
    {
        var token = await new StaticTokenProvider("the-token").ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("the-token");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task ResolveBearerTokenAsync_throws_when_provider_returns_a_blank_token(string blankToken)
    {
        var act = () => new BlankTokenProvider(blankToken).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which;
        thrown.Message.Should().Be("Could not acquire a bearer token from BlankTokenProvider: Token provider BlankTokenProvider returned an empty token.");
        thrown.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_reports_a_provider_failure_as_a_TokenAcquisitionException_carrying_it()
    {
        var failure = new HttpRequestException("idp down");
        var act = () => new ThrowingTokenProvider(failure).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        var thrown = (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which;

        thrown.Message.Should().Be("Could not acquire a bearer token from ThrowingTokenProvider: idp down");
        thrown.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_reports_a_provider_timeout_as_a_TokenAcquisitionException()
    {
        var failure = new TimeoutException("Token acquisition timed out after 30s.");
        var act = () => new ThrowingTokenProvider(failure).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_reports_a_provider_cancelled_by_something_other_than_the_caller_as_a_TokenAcquisitionException()
    {
        var failure = new OperationCanceledException("the provider was disposed");
        var act = () => new ThrowingTokenProvider(failure).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which.InnerException.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_keeps_caller_cancellation_an_OperationCanceledException()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var act = () => new ThrowingTokenProvider(new OperationCanceledException(cancelled.Token))
            .ResolveBearerTokenAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_reports_a_call_deadline_cutting_the_fetch_short_as_a_TokenAcquisitionException()
    {
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();
        var cancelled = new OperationCanceledException(deadline.Token);
        var act = () => new ThrowingTokenProvider(cancelled)
            .ResolveBearerTokenAsync(deadline.Token, callerCancellation: TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which.InnerException.Should().BeSameAs(cancelled);
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_keeps_caller_cancellation_an_OperationCanceledException_when_a_call_deadline_is_also_set()
    {
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();
        var act = () => new ThrowingTokenProvider(new OperationCanceledException(callerCancellation.Token))
            .ResolveBearerTokenAsync(callerCancellation.Token, callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveBearerTokenAsync_passes_a_TokenAcquisitionException_from_the_provider_through_unchanged()
    {
        var failure = new TokenAcquisitionException("already classified", new HttpRequestException("idp down"));
        var act = () => new ThrowingTokenProvider(failure).ResolveBearerTokenAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<TokenAcquisitionException>()).Which.Should().BeSameAs(failure);
    }

    private sealed class ThrowingTokenProvider(Exception failure) : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromException<string>(failure);
    }

    private sealed class BlankTokenProvider(string token) : ITokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(token);
    }
}
