// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Runtime.CompilerServices;
using Canton.Ledger.Kernel.Authentication.TokenGeneration;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Canton.Ledger.Kernel.Tests;

public class ClientCredentialsProviderTests
{
    private static ClientCredentialsOptions CreateOptions(string? audience = null) => new()
    {
        ClientId = "test-client",
        ClientSecret = "test-secret",
        Domain = "https://auth.example.com",
        Audience = audience
    };

    private static ClientCredentialsProvider CreateProvider(
        ClientCredentialsOptions options,
        FakeHttpHandler handler,
        TimeProvider? timeProvider = null,
        ILoggerFactory? loggerFactory = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("CantonAuth").Returns(_ => new HttpClient(handler));
        return new ClientCredentialsProvider(
            Options.Create(options),
            factory,
            timeProvider ?? TimeProvider.System,
            loggerFactory is null ? null : new Logger<ClientCredentialsProvider>(loggerFactory));
    }

    private static async Task<Exception> ObserveFailureAsync(Task<string> call)
    {
        try
        {
            await call;
        }
        catch (Exception failure)
        {
            return failure;
        }

        throw new InvalidOperationException("The token call was expected to fail.");
    }

    public static TheoryData<ClientCredentialsOptions, string> OptionsWithUnresolvableEndpoint => new()
    {
        {
            new ClientCredentialsOptions { ClientId = "test-client", ClientSecret = "test-secret" },
            "*Either TokenEndpoint*"
        },
        {
            new ClientCredentialsOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                TokenEndpoint = new Uri("/oauth/token", UriKind.Relative)
            },
            "TokenEndpoint must be a valid absolute http/https URI."
        },
        {
            new ClientCredentialsOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                TokenEndpoint = new Uri("ftp://auth.example.com/token")
            },
            "TokenEndpoint must be a valid absolute http/https URI."
        },
        {
            new ClientCredentialsOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                Domain = "https://auth.example.com/oauth/token"
            },
            "Domain must not include the /oauth/token path*"
        },
        {
            new ClientCredentialsOptions
            {
                ClientId = "test-client",
                ClientSecret = "test-secret",
                TokenEndpoint = new Uri("http://idp.internal/oauth/token")
            },
            "*plaintext http*AllowInsecureTokenEndpoint*"
        }
    };

    [Theory]
    [MemberData(nameof(OptionsWithUnresolvableEndpoint))]
    public void ClientCredentialsProvider_throws_at_construction_when_endpoint_is_unresolvable(
        ClientCredentialsOptions options,
        string expectedMessage)
    {
        var act = () => CreateProvider(options, new FakeHttpHandler());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(expectedMessage);
    }

    [Fact]
    public async Task GetTokenAsync_sends_correct_form_data_to_TokenEndpoint()
    {
        var handler = new FakeHttpHandler();
        var options = CreateOptions();
        using var provider = CreateProvider(options, handler);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri.Should().Be(new Uri("https://auth.example.com/oauth/token"));
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequestBody.Should().Contain("grant_type=client_credentials");
        handler.LastRequestBody.Should().Contain("client_id=test-client");
        handler.LastRequestBody.Should().Contain("client_secret=test-secret");
    }

    [Fact]
    public async Task GetTokenAsync_includes_Audience_when_configured()
    {
        var handler = new FakeHttpHandler();
        var options = CreateOptions(audience: "https://canton.network/");
        using var provider = CreateProvider(options, handler);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        handler.LastRequestBody.Should().Contain("audience=https%3A%2F%2Fcanton.network%2F");
    }

    [Fact]
    public async Task GetTokenAsync_returns_access_token_from_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"my-real-token","expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("my-real-token");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_http_error()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.Unauthorized,
            """{"error":"invalid_client"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_malformed_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            "not-json");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetTokenAsync_returns_cached_token_when_not_expired()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        var token1 = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var token2 = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token1.Should().Be(token2);
        handler.CallCount.Should().Be(1, "second call should use cache");
    }

    [Fact]
    public async Task GetTokenAsync_refreshes_token_when_within_SafetyMargin()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.SafetyMargin = TimeSpan.FromSeconds(30);
        using var provider = CreateProvider(options, handler, timeProvider);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromSeconds(3571));

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(2, "token should be refreshed when within safety margin");
    }

    [Fact]
    public async Task GetTokenAsync_concurrent_callers_share_single_refresh()
    {
        var handler = new FakeHttpHandler();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromHours(2));

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("fake-token");
        handler.CallCount.Should().Be(2, "concurrent callers should share a single HTTP request after cache expiry");
    }

    [Fact]
    public async Task GetTokenAsync_concurrent_callers_share_one_failed_refresh()
    {
        var handler = new FakeHttpHandler()
            .WithResponse(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}""")
            .WithDelay(TimeSpan.FromMilliseconds(200));
        using var provider = CreateProvider(CreateOptions(), handler);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks.Select(ObserveFailureAsync));

        outcomes.Should().AllBeOfType<HttpRequestException>();
        outcomes.Cast<HttpRequestException>().Select(outcome => outcome.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.ServiceUnavailable);
        handler.CallCount.Should().Be(1, "ten concurrent callers must share one failed refresh, not retry ten times");
    }

    [Fact]
    public async Task GetTokenAsync_concurrent_callers_share_one_timed_out_refresh()
    {
        var handler = new FakeHttpHandler().WithDelay(Timeout.InfiniteTimeSpan);
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMilliseconds(100);
        using var provider = CreateProvider(options, handler);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => provider.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks.Select(ObserveFailureAsync));

        outcomes.Should().AllBeOfType<TimeoutException>();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_fetches_again_on_a_call_after_a_failed_refresh_has_finished_when_TokenFailureCooldown_is_disabled()
    {
        var handler = new FakeHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}");
        var options = CreateOptions();
        options.TokenFailureCooldown = new TokenFailureCooldownOptions { Enabled = false };
        using var provider = CreateProvider(options, handler);

        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        handler.CallCount.Should().Be(2);
    }

    private static FakeHttpHandler UnavailableIdentityProvider() =>
        new FakeHttpHandler().WithResponse(HttpStatusCode.ServiceUnavailable, "{}");

    [Fact]
    public async Task GetTokenAsync_fails_fast_with_the_same_failure_within_the_cooldown_window_without_calling_the_endpoint()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);

        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMilliseconds(499));
        var failure = await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        failure.Should().BeOfType<HttpRequestException>().Which.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_fetches_again_once_the_first_cooldown_window_of_500_milliseconds_has_elapsed()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);

        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMilliseconds(500));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTokenAsync_doubles_the_cooldown_window_on_each_consecutive_failure()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);

        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMilliseconds(500));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(2);

        time.Advance(TimeSpan.FromMilliseconds(999));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(2, "the second window is 1000 milliseconds");

        time.Advance(TimeSpan.FromMilliseconds(1));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(3);

        time.Advance(TimeSpan.FromMilliseconds(1999));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(3, "the third window is 2000 milliseconds");

        time.Advance(TimeSpan.FromMilliseconds(1));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(4);
    }

    [Fact]
    public async Task GetTokenAsync_stops_doubling_the_cooldown_window_at_MaxDelay()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.TokenFailureCooldown = new TokenFailureCooldownOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(3)
        };
        using var provider = CreateProvider(options, handler, time);

        foreach (var elapsedBeforeFetch in new[] { 0, 1000, 2000, 3000 })
        {
            time.Advance(TimeSpan.FromMilliseconds(elapsedBeforeFetch));
            await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        }
        handler.CallCount.Should().Be(4);

        time.Advance(TimeSpan.FromMilliseconds(2999));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(4, "the fourth window is capped at 3000 milliseconds, not 4000");

        time.Advance(TimeSpan.FromMilliseconds(1));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTokenAsync_does_not_overflow_the_cooldown_window_after_many_consecutive_failures()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);

        for (var failure = 0; failure < 200; failure++)
        {
            time.Advance(TimeSpan.FromSeconds(30));
            await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        }

        time.Advance(TimeSpan.FromSeconds(29));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(200);

        time.Advance(TimeSpan.FromSeconds(1));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(201);
    }

    [Fact]
    public async Task GetTokenAsync_restarts_the_cooldown_at_InitialDelay_after_a_successful_fetch()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMilliseconds(500));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        time.Advance(TimeSpan.FromMilliseconds(1000));
        handler.WithResponse(HttpStatusCode.OK, """{"access_token":"recovered","expires_in":3600,"token_type":"Bearer"}""");
        (await provider.GetTokenAsync(TestContext.Current.CancellationToken)).Should().Be("recovered");

        time.Advance(TimeSpan.FromHours(2));
        handler.WithResponse(HttpStatusCode.ServiceUnavailable, "{}");
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(4);

        time.Advance(TimeSpan.FromMilliseconds(499));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(4);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        handler.CallCount.Should().Be(5, "the success reset the window to 500 milliseconds instead of 2000");
    }

    [Fact]
    public async Task GetTokenAsync_returns_the_token_on_the_first_call_after_the_window_when_the_endpoint_recovered()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        handler.WithResponse(HttpStatusCode.OK, """{"access_token":"recovered","expires_in":3600,"token_type":"Bearer"}""");
        time.Advance(TimeSpan.FromMilliseconds(500));

        (await provider.GetTokenAsync(TestContext.Current.CancellationToken)).Should().Be("recovered");
    }

    [Fact]
    public async Task GetTokenAsync_does_not_grow_the_failure_stack_trace_across_calls_inside_one_cooldown_window()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        var stackTraceLengths = new List<int>();
        for (var call = 2; call <= 100; call++)
        {
            var failure = await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
            stackTraceLengths.Add(failure.StackTrace!.Length);
        }

        stackTraceLengths.Last().Should().Be(stackTraceLengths.First());
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public void GetTokenAsync_leaves_no_unobserved_task_exception_when_a_cooldown_window_passes_without_further_calls()
    {
        var unobserved = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs args) => unobserved.AddRange(args.Exception.InnerExceptions);
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            var abandonedProvider = OpenCooldownWindowAndAbandonProvider();
            CollectGarbageUntilReclaimed(abandonedProvider);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }

        unobserved.OfType<HttpRequestException>().Should().NotContain(failure => failure.StatusCode == HttpStatusCode.Gone);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenCooldownWindowAndAbandonProvider()
    {
        var handler = new FakeHttpHandler().WithResponse(HttpStatusCode.Gone, "{}");
        var provider = CreateProvider(CreateOptions(), handler, new FakeTimeProvider(DateTimeOffset.UtcNow));
        ObserveFailureAsync(provider.GetTokenAsync(CancellationToken.None)).GetAwaiter().GetResult();
        return new WeakReference(provider);
    }

    private static void CollectGarbageUntilReclaimed(WeakReference reference)
    {
        for (var attempt = 0; attempt < 40 && reference.IsAlive; attempt++)
        {
            Thread.Sleep(50);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        reference.IsAlive.Should().BeFalse("the finalizer of an unobserved task exception can only run once the provider is gone");
    }

    [Fact]
    public async Task GetTokenAsync_concurrent_callers_during_the_cooldown_window_share_the_failure_without_calling_the_endpoint()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => provider.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks.Select(ObserveFailureAsync));

        outcomes.Should().AllBeOfType<HttpRequestException>();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_fails_fast_with_a_TimeoutException_within_the_window_after_a_timed_out_fetch()
    {
        var handler = new FakeHttpHandler().WithDelay(Timeout.InfiniteTimeSpan);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMilliseconds(100);
        using var provider = CreateProvider(options, handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        var failure = await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));

        failure.Should().BeOfType<TimeoutException>();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_propagates_caller_cancellation_instead_of_the_cooldown_failure()
    {
        var handler = UnavailableIdentityProvider();
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, time);
        await ObserveFailureAsync(provider.GetTokenAsync(TestContext.Current.CancellationToken));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var failure = await ObserveFailureAsync(provider.GetTokenAsync(canceled.Token));

        failure.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task GetTokenAsync_canceling_one_waiter_does_not_cancel_the_shared_refresh()
    {
        var handler = new FakeHttpHandler().WithDelay(TimeSpan.FromMilliseconds(200));
        using var provider = CreateProvider(CreateOptions(), handler);
        using var cancelingWaiter = new CancellationTokenSource();

        var canceled = provider.GetTokenAsync(cancelingWaiter.Token);
        var patient = provider.GetTokenAsync(TestContext.Current.CancellationToken);
        cancelingWaiter.Cancel();

        await ((Func<Task>)(() => canceled)).Should().ThrowAsync<OperationCanceledException>();
        (await patient).Should().Be("fake-token");
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_aborts_an_in_flight_refresh_for_every_waiter_as_cancellation()
    {
        var handler = new FakeHttpHandler().WithDelay(Timeout.InfiniteTimeSpan);
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMinutes(5);
        var provider = CreateProvider(options, handler);
        var waiting = provider.GetTokenAsync(TestContext.Current.CancellationToken);

        provider.Dispose();

        var failure = await ObserveFailureAsync(waiting);
        failure.Should().BeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        var provider = CreateProvider(CreateOptions(), new FakeHttpHandler());

        provider.Dispose();

        var secondDispose = provider.Dispose;
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task GetTokenAsync_caches_for_half_the_lifetime_when_SafetyMargin_exceeds_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"short-lived","expires_in":20,"token_type":"Bearer"}""");
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.SafetyMargin = TimeSpan.FromSeconds(30);
        using var provider = CreateProvider(options, handler, timeProvider);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(9));
        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(1, "a 20 s token is cached for 10 s however large SafetyMargin is");

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTokenAsync_caches_for_half_the_lifetime_when_SafetyMargin_equals_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"short-lived","expires_in":30,"token_type":"Bearer"}""");
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = CreateOptions();
        options.SafetyMargin = TimeSpan.FromSeconds(30);
        using var provider = CreateProvider(options, handler, timeProvider);

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromSeconds(14));
        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTokenAsync_publishes_refreshed_token_before_new_expiry_so_no_reader_sees_stale_token()
    {
        var handler = new FakeHttpHandler().WithResponseSequence(
            """{"access_token":"token-v1","expires_in":3600,"token_type":"Bearer"}""",
            """{"access_token":"token-v2","expires_in":3600,"token_type":"Bearer"}""");
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        first.Should().Be("token-v1");

        timeProvider.Advance(TimeSpan.FromHours(2));

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => provider.GetTokenAsync(TestContext.Current.CancellationToken))
            .ToArray();

        var tokens = await Task.WhenAll(tasks);

        tokens.Should().AllBe("token-v2", "no caller should see the old token after refresh");
    }

    [Fact]
    public async Task GetTokenAsync_reader_paused_mid_cache_check_never_returns_stale_token_once_refresh_publishes()
    {
        var handler = new FakeHttpHandler().WithResponseSequence(
            """{"access_token":"token-v1","expires_in":3600,"token_type":"Bearer"}""",
            """{"access_token":"token-v2","expires_in":3600,"token_type":"Bearer"}""");
        var timeProvider = new PausableTimeProvider(DateTimeOffset.UtcNow);
        using var provider = CreateProvider(CreateOptions(), handler, timeProvider);
        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromHours(2));

        timeProvider.PauseNextRead();
        var pausedReader = Task.Run(
            () => provider.GetTokenAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        await timeProvider.ReadPaused;
        var refreshed = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        timeProvider.ResumePausedRead();
        var resumed = await pausedReader;

        refreshed.Should().Be("token-v2");
        resumed.Should().Be("token-v2", "a reader that began before the refresh published must not pair the old token with the new expiry");
    }

    private sealed class PausableTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private readonly TaskCompletionSource _readPaused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _pauseArmed;

        public Task ReadPaused => _readPaused.Task;

        public void PauseNextRead() => Volatile.Write(ref _pauseArmed, 1);

        public void ResumePausedRead() => _resume.SetResult();

        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Exchange(ref _pauseArmed, 0) == 1)
            {
                _readPaused.SetResult();
                _resume.Task.Wait(TestContext.Current.CancellationToken);
            }

            return base.GetUtcNow();
        }
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_zero_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"token","expires_in":0,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid expires_in*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_negative_expires_in()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"token","expires_in":-1,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid expires_in*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_empty_access_token_in_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"access_token":"","expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no access_token*");
    }

    [Fact]
    public async Task GetTokenAsync_throws_on_null_access_token_in_response()
    {
        var handler = new FakeHttpHandler().WithResponse(
            HttpStatusCode.OK,
            """{"expires_in":3600,"token_type":"Bearer"}""");
        using var provider = CreateProvider(CreateOptions(), handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no access_token*");
    }

    [Fact]
    public async Task GetTokenAsync_propagates_cancellation()
    {
        var handler = new FakeHttpHandler();
        using var provider = CreateProvider(CreateOptions(), handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => provider.GetTokenAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetTokenAsync_faults_within_TokenAcquisitionTimeout_when_endpoint_stalls()
    {
        var handler = new FakeHttpHandler().WithDelay(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMilliseconds(50);
        using var provider = CreateProvider(options, handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<TimeoutException>()).Which;
        exception.Message.Should().Contain("Token acquisition")
            .And.Contain("auth.example.com/oauth/token")
            .And.Contain("timed out");
    }

    [Fact]
    public async Task GetTokenAsync_timeout_message_does_not_leak_the_client_secret()
    {
        var handler = new FakeHttpHandler().WithDelay(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMilliseconds(50);
        using var provider = CreateProvider(options, handler);

        var act = () => provider.GetTokenAsync(TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<TimeoutException>()).Which;
        exception.Message.Should().NotContain(options.ClientSecret);
    }

    [Fact]
    public async Task GetTokenAsync_propagates_caller_cancellation_even_while_endpoint_stalls()
    {
        var handler = new FakeHttpHandler().WithDelay(Timeout.InfiniteTimeSpan);
        var options = CreateOptions();
        options.TokenAcquisitionTimeout = TimeSpan.FromMinutes(5);
        using var provider = CreateProvider(options, handler);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => provider.GetTokenAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_warns_when_token_endpoint_uses_plaintext_http()
    {
        var options = new ClientCredentialsOptions
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            TokenEndpoint = new Uri("http://localhost:8080/oauth/token"),
            AllowInsecureTokenEndpoint = true
        };
        var loggerFactory = new CapturingLoggerFactory();

        using var provider = CreateProvider(options, new FakeHttpHandler(), loggerFactory: loggerFactory);

        loggerFactory.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning
            && r.Message.Contains("plaintext http")
            && r.Message.Contains("client secret"));
    }

    [Fact]
    public void Constructor_does_not_warn_when_token_endpoint_uses_https()
    {
        var loggerFactory = new CapturingLoggerFactory();

        using var provider = CreateProvider(CreateOptions(), new FakeHttpHandler(), loggerFactory: loggerFactory);

        loggerFactory.Records.Should().NotContain(r => r.Level == LogLevel.Warning);
    }
}
