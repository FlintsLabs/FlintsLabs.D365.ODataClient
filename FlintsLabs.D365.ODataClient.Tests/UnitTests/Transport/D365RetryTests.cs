using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Transport;

public class D365RetryTests
{
    [Fact]
    public async Task ReadRetry_IsDisabledByDefault()
    {
        var (transport, handler) = CreateTransport(CreateOptions());
        handler.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Enqueue(HttpStatusCode.OK);

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task OptInReadRetry_RetriesSupportedStatuses(HttpStatusCode statusCode)
    {
        var options = CreateOptions(maxReadRetries: 1);
        var (transport, handler) = CreateTransport(options);
        handler.Enqueue(statusCode);
        handler.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OptInReadRetry_RetriesTransientTransportFailure()
    {
        var (transport, handler) = CreateTransport(CreateOptions(maxReadRetries: 1));
        handler.EnqueueException(new HttpRequestException("temporary network error"));
        handler.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OptInReadRetry_RetriesPerAttemptTimeout()
    {
        var options = CreateOptions(maxReadRetries: 1);
        options.RequestTimeout = TimeSpan.FromMilliseconds(20);
        var (transport, handler) = CreateTransport(options);
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        handler.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OptInReadRetry_AppliesToHeadRequests()
    {
        var (transport, handler) = CreateTransport(CreateOptions(maxReadRetries: 1));
        handler.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Enqueue(HttpStatusCode.OK);
        var request = new D365Request(
            HttpMethod.Head,
            "Entities",
            null,
            "Entities",
            new Dictionary<string, string>());

        var response = await transport.SendRawAsync(request, default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Mutation_IsNeverRetriedByReadRetryPolicy()
    {
        var (transport, handler) = CreateTransport(CreateOptions(maxReadRetries: 3));
        handler.Enqueue(HttpStatusCode.ServiceUnavailable);
        handler.Enqueue(HttpStatusCode.NoContent);

        var response = await transport.SendRawAsync(
            D365Request.Json(HttpMethod.Patch, "Entities(1)", "{}", "Entities"),
            default);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void RetryDelay_HonorsDeltaRetryAfterAndMaxDelay()
    {
        var options = CreateOptions(maxReadRetries: 1).Retry;
        options.MaxDelay = TimeSpan.FromSeconds(2);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = ["120"]
        };

        var delay = D365RetryPolicy.CalculateDelay(
            headers,
            retryNumber: 1,
            options,
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"));

        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Fact]
    public void RetryDelay_HonorsRetryAfterHttpDate()
    {
        var now = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        var options = CreateOptions(maxReadRetries: 1).Retry;
        options.MaxDelay = TimeSpan.FromSeconds(30);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Retry-After"] = [now.AddSeconds(7).ToString("R")]
        };

        var delay = D365RetryPolicy.CalculateDelay(headers, 1, options, now);

        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    [Fact]
    public void RetryDelay_UsesBoundedExponentialBackoff()
    {
        var options = CreateOptions(maxReadRetries: 3).Retry;
        options.BaseDelay = TimeSpan.FromMilliseconds(100);
        options.MaxDelay = TimeSpan.FromMilliseconds(250);

        var delay = D365RetryPolicy.CalculateDelay(
            new Dictionary<string, string[]>(),
            retryNumber: 3,
            options,
            DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.FromMilliseconds(250), delay);
    }

    [Fact]
    public async Task CancellationDuringRetryDelay_StopsFurtherRequests()
    {
        var retryResponseReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var options = CreateOptions(maxReadRetries: 3);
        options.Retry.BaseDelay = TimeSpan.FromSeconds(30);
        options.Retry.MaxDelay = TimeSpan.FromSeconds(30);
        var (transport, handler) = CreateTransport(options);
        handler.Enqueue((_, _) =>
        {
            retryResponseReturned.SetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        using var cancellation = new CancellationTokenSource();

        var task = transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            cancellation.Token);
        await retryResponseReturned.Task;
        cancellation.Cancel();

        await Assert.ThrowsAsync<D365OperationCanceledException>(() => task);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(-1, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 3, 2)]
    public void Registration_RejectsInvalidRetryOptions(
        int maxRetries,
        int baseDelaySeconds,
        int maxDelaySeconds)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddD365ODataClient(builder => builder
                .WithResource("https://example.test")
                .ConfigureRetry(retry =>
                {
                    retry.MaxReadRetries = maxRetries;
                    retry.BaseDelay = TimeSpan.FromSeconds(baseDelaySeconds);
                    retry.MaxDelay = TimeSpan.FromSeconds(maxDelaySeconds);
                })));
    }

    private static (D365Transport Transport, StubHttpMessageHandler Handler) CreateTransport(
        D365ClientOptions options)
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
                NullLogger.Instance,
                new StubTokenProvider("token"),
                options),
            handler);
    }

    private static D365ClientOptions CreateOptions(int maxReadRetries = 0)
    {
        return new D365ClientOptions
        {
            Resource = "https://example.test",
            RequestTimeout = TimeSpan.FromSeconds(5),
            Retry = new D365RetryOptions
            {
                MaxReadRetries = maxReadRetries,
                BaseDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(1),
                UseJitter = false
            }
        };
    }
}
