using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Transport;

public class D365TransportTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task RawTransport_ReturnsEveryReceivedHttpResponse(
        HttpStatusCode statusCode,
        bool expectedSuccess)
    {
        var (transport, handler) = CreateTransport();
        handler.Enqueue(statusCode, "{\"value\":[]}");

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            CancellationToken.None);

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(expectedSuccess, response.IsSuccessStatusCode);
        Assert.Equal("{\"value\":[]}", response.RawBody);
    }

    [Fact]
    public async Task RawTransport_CapturesResponseHeadersAndRequestId()
    {
        var (transport, handler) = CreateTransport();
        handler.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
            response.Headers.TryAddWithoutValidation("x-ms-service-request-id", "request-123");
            response.Headers.TryAddWithoutValidation("Preference-Applied", "return=representation");
            return Task.FromResult(response);
        });

        var response = await transport.SendRawAsync(
            D365Request.Get("Entities", "Entities"),
            default);

        Assert.Equal("request-123", response.RequestId);
        Assert.Equal("return=representation", Assert.Single(response.Headers["Preference-Applied"]));
        Assert.Equal("https://example.test/data/Entities", response.RequestUri.ToString());
    }

    [Fact]
    public async Task EnsuredTransport_ThrowsParsedD365HttpException()
    {
        var (transport, handler) = CreateTransport();
        handler.Enqueue(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":\"0x8001\",\"message\":\"invalid key\"}}");

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            transport.SendEnsuredAsync(
                D365Request.Get("Entities", "Entities"),
                default));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(HttpMethod.Get, exception.Method);
        Assert.Equal("Entities", exception.EntityName);
        Assert.Equal("0x8001", exception.D365ErrorCode);
        Assert.Equal("invalid key", exception.D365ErrorMessage);
        Assert.Equal(D365MutationOutcome.NotApplicable, exception.MutationOutcome);
        Assert.False(exception.IsTransient);
    }

    [Fact]
    public async Task EnsuredTransport_TruncatesErrorBodyStoredInException()
    {
        var options = CreateOptions();
        options.MaxErrorBodyBytes = 8;
        var (transport, handler) = CreateTransport(options);
        handler.Enqueue(HttpStatusCode.BadRequest, "1234567890");

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            transport.SendEnsuredAsync(
                D365Request.Get("Entities", "Entities"),
                default));

        Assert.Equal("12345678", exception.ResponseBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, D365MutationOutcome.SucceededOrAccepted)]
    [InlineData(HttpStatusCode.BadRequest, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, D365MutationOutcome.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, D365MutationOutcome.Unknown)]
    public async Task RawTransport_ClassifiesMutationOutcome(
        HttpStatusCode statusCode,
        D365MutationOutcome expectedOutcome)
    {
        var (transport, handler) = CreateTransport();
        handler.Enqueue(statusCode);

        var response = await transport.SendRawAsync(
            D365Request.Json(HttpMethod.Post, "Entities", "{}", "Entities"),
            default);

        Assert.Equal(expectedOutcome, response.MutationOutcome);
    }

    [Fact]
    public async Task Transport_HttpRequestException_ThrowsTypedTransportException()
    {
        var (transport, handler) = CreateTransport();
        handler.EnqueueException(new HttpRequestException("network down"));

        var exception = await Assert.ThrowsAsync<D365TransportException>(() =>
            transport.SendRawAsync(
                D365Request.Get("Entities", "Entities"),
                default));

        Assert.Equal(D365FailureKind.Transport, exception.FailureKind);
        Assert.True(exception.IsTransient);
        Assert.Equal(D365MutationOutcome.NotApplicable, exception.MutationOutcome);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task Transport_InternalTimeout_ThrowsTimeoutExceptionWithUnknownMutationOutcome()
    {
        var options = CreateOptions();
        options.RequestTimeout = TimeSpan.FromMilliseconds(20);
        var (transport, handler) = CreateTransport(options);
        handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var exception = await Assert.ThrowsAsync<D365TransportException>(() =>
            transport.SendRawAsync(
                D365Request.Json(HttpMethod.Patch, "Entities(1)", "{}", "Entities"),
                default));

        Assert.Equal(D365FailureKind.Timeout, exception.FailureKind);
        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
    }

    [Fact]
    public async Task Transport_CallerCancellation_RemainsOperationCanceledException()
    {
        var (transport, handler) = CreateTransport();
        handler.Enqueue(HttpStatusCode.OK);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<D365OperationCanceledException>(() =>
            transport.SendRawAsync(
                D365Request.Get("Entities", "Entities"),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(D365MutationOutcome.NotApplicable, exception.MutationOutcome);
        Assert.Empty(handler.Requests);
    }

    private static (D365Transport Transport, StubHttpMessageHandler Handler) CreateTransport(
        D365ClientOptions? options = null)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/data/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var transport = new D365Transport(
            factory.Object,
            Mock.Of<ILogger>(),
            new StubTokenProvider("token"),
            options ?? CreateOptions());

        return (transport, handler);
    }

    private static D365ClientOptions CreateOptions()
    {
        return new D365ClientOptions
        {
            Resource = "https://example.test",
            RequestTimeout = TimeSpan.FromSeconds(5)
        };
    }
}
