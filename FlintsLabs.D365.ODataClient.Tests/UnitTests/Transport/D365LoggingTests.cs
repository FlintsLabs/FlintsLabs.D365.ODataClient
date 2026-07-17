using System.Collections.Concurrent;
using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Transport;

public class D365LoggingTests
{
    [Fact]
    public async Task LogsContainDiagnosticsWithoutSensitiveValues()
    {
        var logger = new CapturingLogger();
        var (transport, handler) = CreateTransport(logger);
        handler.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":[]}")
            };
            response.Headers.TryAddWithoutValidation("x-ms-service-request-id", "request-log-1");
            return Task.FromResult(response);
        });
        var url = "Entities?$filter=Name%20eq%20%27top-secret-filter%27&$select=Name";

        await transport.SendRawAsync(D365Request.Get(url, "Entities"), default);

        var log = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("GET", log);
        Assert.Contains("Entities", log);
        Assert.Contains("OK", log);
        Assert.Contains("request-log-1", log);
        Assert.Contains("NotApplicable", log);
        Assert.Contains("$filter", log);
        Assert.Contains("$select", log);
        Assert.Contains("ms", log);
        Assert.DoesNotContain("top-secret-filter", log);
        Assert.DoesNotContain("token-secret", log);
        Assert.DoesNotContain("client-secret", log);
    }

    [Fact]
    public async Task LogsExcludePayloadResponseBodyCookiesAndKeyValues()
    {
        var logger = new CapturingLogger();
        var (transport, handler) = CreateTransport(logger);
        handler.Enqueue(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"server-secret-body\"}}");
        var request = new D365Request(
            HttpMethod.Patch,
            "Entities(Id='secret-key')?$filter=Name%20eq%20%27secret-filter%27",
            "{\"password\":\"payload-secret\"}",
            "Entities",
            new Dictionary<string, string> { ["Cookie"] = "cookie-secret" });

        var response = await transport.SendRawAsync(request, default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var log = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("Entities(*)", log);
        Assert.DoesNotContain("secret-key", log);
        Assert.DoesNotContain("secret-filter", log);
        Assert.DoesNotContain("payload-secret", log);
        Assert.DoesNotContain("server-secret-body", log);
        Assert.DoesNotContain("cookie-secret", log);
    }

    [Fact]
    public async Task LoggerFailureDoesNotReplaceD365Exception()
    {
        var (transport, handler) = CreateTransport(new ThrowingLogger());
        handler.Enqueue(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":\"root-error\",\"message\":\"invalid\"}}");

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            transport.SendEnsuredAsync(
                D365Request.Get("Entities", "Entities"),
                default));

        Assert.Equal("root-error", exception.D365ErrorCode);
    }

    [Fact]
    public void SanitizerKeepsOnlyRouteShapeAndQueryOptionNames()
    {
        var uri = new Uri(
            "https://user:password@example.test/data/Entities(Id='secret-key')" +
            "?$filter=Name%20eq%20%27secret-filter%27&$select=Name#fragment");

        var sanitized = D365LogSanitizer.Sanitize(uri);

        Assert.Equal(
            "https://example.test/data/Entities(*)?$filter&$select",
            sanitized);
    }

    private static (D365Transport Transport, StubHttpMessageHandler Handler) CreateTransport(
        ILogger logger)
    {
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/data/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var options = new D365ClientOptions
        {
            Resource = "https://example.test",
            ClientSecret = "client-secret",
            RequestTimeout = TimeSpan.FromSeconds(5)
        };

        return (
            new D365Transport(
                factory.Object,
                logger,
                new StubTokenProvider("token-secret"),
                options),
            handler);
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue(formatter(state, exception));
        }
    }
}
