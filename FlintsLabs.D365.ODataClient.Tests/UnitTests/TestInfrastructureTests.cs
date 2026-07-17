using System.Net;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class TestInfrastructureTests
{
    [Fact]
    public async Task Handler_ReturnsQueuedResponsesInOrderAndCapturesRequests()
    {
        var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized, "first");
        handler.Enqueue(HttpStatusCode.OK, "second");
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/")
        };

        using var first = await client.GetAsync("first");
        using var second = await client.PostAsync(
            "second",
            new StringContent("payload"));

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("https://example.test/first", request.RequestUri?.ToString()),
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("payload", request.Body);
            });
    }

    [Fact]
    public async Task Handler_PropagatesQueuedException()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("network down"));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/")
        };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("entities"));

        Assert.Equal("network down", exception.Message);
    }

    [Fact]
    public async Task TokenProvider_ReturnsConfiguredTokenAndCountsRequests()
    {
        var provider = new StubTokenProvider("token-1");

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal("token-1", first);
        Assert.Equal("token-1", second);
        Assert.Equal(2, provider.GetCount);
    }

    [Fact]
    public void ThrowingLogger_ThrowsFromLog()
    {
        ILogger logger = new ThrowingLogger();

        Assert.Throws<InvalidOperationException>(() =>
            logger.LogInformation("message"));
    }
}
