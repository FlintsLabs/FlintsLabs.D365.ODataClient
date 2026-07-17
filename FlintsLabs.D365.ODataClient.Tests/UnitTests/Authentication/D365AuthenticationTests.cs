using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Authentication;

public class D365AuthenticationTests
{
    [Fact]
    public async Task RawTransport_RefreshesAndRetriesOneActual401()
    {
        var tokenProvider = new StubTokenProvider("stale-token", "fresh-token");
        var (transport, handler) = CreateTransport(tokenProvider);
        handler.Enqueue(HttpStatusCode.Unauthorized);
        handler.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, tokenProvider.GetCount);
        Assert.Equal(1, tokenProvider.RefreshCount);
        Assert.Equal("stale-token", Assert.Single(tokenProvider.RejectedTokens));
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("Bearer stale-token", Assert.Single(request.Headers["Authorization"])),
            request => Assert.Equal("Bearer fresh-token", Assert.Single(request.Headers["Authorization"])));
    }

    [Fact]
    public async Task RawTransport_ReturnsSecond401WithoutAnotherRefresh()
    {
        var tokenProvider = new StubTokenProvider("stale-token", "fresh-token");
        var (transport, handler) = CreateTransport(tokenProvider);
        handler.Enqueue(HttpStatusCode.Unauthorized);
        handler.Enqueue(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"still unauthorized\"}}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, tokenProvider.RefreshCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task EnsuredTransport_ThrowsAuthenticationExceptionAfterSecond401()
    {
        var tokenProvider = new StubTokenProvider("stale-token", "fresh-token");
        var (transport, handler) = CreateTransport(tokenProvider);
        handler.Enqueue(HttpStatusCode.Unauthorized);
        handler.Enqueue(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"invalid_token\",\"message\":\"expired\"}}");

        var exception = await Assert.ThrowsAsync<D365AuthenticationException>(() =>
            transport.SendEnsuredAsync(
                D365Request.Get("Entities", "Entities"),
                default));

        Assert.Equal("invalid_token", exception.D365ErrorCode);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(1, tokenProvider.RefreshCount);
    }

    [Fact]
    public async Task Transport_RetriesMutationOnlyAfterActual401()
    {
        var tokenProvider = new StubTokenProvider("stale-token", "fresh-token");
        var (transport, handler) = CreateTransport(tokenProvider);
        handler.Enqueue(HttpStatusCode.Unauthorized);
        handler.Enqueue(HttpStatusCode.NoContent);

        var response = await transport.SendRawAsync(
            D365Request.Json(HttpMethod.Patch, "Entities(1)", "{\"name\":\"updated\"}", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, response.MutationOutcome);
        Assert.Equal(1, tokenProvider.RefreshCount);
        Assert.All(handler.Requests, request => Assert.Equal("{\"name\":\"updated\"}", request.Body));
    }

    [Fact]
    public async Task Transport_PropagatesCallerCancellationDuringRefresh()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenProvider = new StubTokenProvider("stale-token", "fresh-token")
        {
            RefreshOverride = async (_, cancellationToken) =>
            {
                refreshStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new D365AccessToken("never", DateTimeOffset.MaxValue);
            }
        };
        var (transport, handler) = CreateTransport(tokenProvider);
        handler.Enqueue(HttpStatusCode.Unauthorized);
        using var cancellation = new CancellationTokenSource();

        var responseTask = transport.SendRawAsync(
            D365Request.Json(HttpMethod.Post, "Entities", "{}", "Entities"),
            cancellation.Token);
        await refreshStarted.Task;
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<D365OperationCanceledException>(() => responseTask);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TokenProvider_ConcurrentRejectedTokenRefreshUsesSingleAuthorityCall()
    {
        var acquisitionCount = 0;
        var provider = new D365AccessTokenProvider(
            NullLogger<D365AccessTokenProvider>.Instance,
            new D365ClientOptions(),
            async cancellationToken =>
            {
                var sequence = Interlocked.Increment(ref acquisitionCount);
                await Task.Delay(20, cancellationToken);
                return new D365AccessToken($"token-{sequence}", DateTimeOffset.UtcNow.AddHours(1));
            });
        var staleToken = await provider.GetAccessTokenAsync();

        var refreshedTokens = await Task.WhenAll(
            Enumerable.Range(0, 20)
                .Select(_ => provider.RefreshAccessTokenAsync(staleToken.Value).AsTask()));

        Assert.Equal("token-1", staleToken.Value);
        Assert.Equal(2, acquisitionCount);
        Assert.All(refreshedTokens, token => Assert.Equal("token-2", token.Value));
    }

    [Fact]
    public async Task TokenProvider_CancellationStopsTokenAcquisition()
    {
        var acquisitionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new D365AccessTokenProvider(
            NullLogger<D365AccessTokenProvider>.Instance,
            new D365ClientOptions(),
            async cancellationToken =>
            {
                acquisitionStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new D365AccessToken("never", DateTimeOffset.MaxValue);
            });
        using var cancellation = new CancellationTokenSource();

        var tokenTask = provider.GetAccessTokenAsync(cancellation.Token).AsTask();
        await acquisitionStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tokenTask);
    }

    private static (D365Transport Transport, StubHttpMessageHandler Handler) CreateTransport(
        ID365AccessTokenProvider tokenProvider)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/data/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return (
            new D365Transport(
                factory.Object,
                Mock.Of<ILogger>(),
                tokenProvider,
                new D365ClientOptions
                {
                    Resource = "https://example.test",
                    RequestTimeout = TimeSpan.FromSeconds(5)
                }),
            handler);
    }
}
