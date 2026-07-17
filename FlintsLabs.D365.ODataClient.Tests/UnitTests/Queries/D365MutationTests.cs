using System.Net;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Queries;

public class D365MutationTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task AddAsync_ReturnsEverySuccessfulResponse(HttpStatusCode statusCode)
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(statusCode, statusCode == HttpStatusCode.NoContent ? "" : "{}");

        var response = await harness.Query.AddAsync(new TestEntity { Name = "created" });

        Assert.Equal(statusCode, response.StatusCode);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, response.MutationOutcome);
        Assert.Equal(HttpMethod.Post, Assert.Single(harness.Handler.Requests).Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task UpdateAndDelete_ReturnSuccessfulResponses(HttpStatusCode statusCode)
    {
        var update = CreateHarness<TestEntity>();
        update.Handler.Enqueue(statusCode);
        var updateResponse = await update.Query
            .AddIdentity("Id", Guid.Parse("20136305-68d1-ef11-8ee9-000d3aa08849"))
            .UpdateAsync(new { Name = "updated" });

        var delete = CreateHarness<TestEntity>();
        delete.Handler.Enqueue(statusCode);
        var deleteResponse = await delete.Query
            .AddIdentity("Id", Guid.Parse("20136305-68d1-ef11-8ee9-000d3aa08849"))
            .DeleteAsync();

        Assert.Equal(statusCode, updateResponse.StatusCode);
        Assert.Equal(statusCode, deleteResponse.StatusCode);
        Assert.Equal(HttpMethod.Patch, Assert.Single(update.Handler.Requests).Method);
        Assert.Equal(HttpMethod.Delete, Assert.Single(delete.Handler.Requests).Method);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.Forbidden, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.Conflict, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.PreconditionFailed, D365MutationOutcome.Rejected)]
    [InlineData((HttpStatusCode)422, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, D365MutationOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, D365MutationOutcome.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, D365MutationOutcome.Unknown)]
    [InlineData(HttpStatusCode.BadGateway, D365MutationOutcome.Unknown)]
    [InlineData(HttpStatusCode.ServiceUnavailable, D365MutationOutcome.Unknown)]
    [InlineData(HttpStatusCode.GatewayTimeout, D365MutationOutcome.Unknown)]
    public async Task Mutation_NonSuccessThrowsAndIsNeverRetried(
        HttpStatusCode statusCode,
        D365MutationOutcome expectedOutcome)
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(
            statusCode,
            "{\"error\":{\"code\":\"mutation_failed\",\"message\":\"rejected\"}}");

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            harness.Query.AddAsync(new { Name = "value" }));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal("mutation_failed", exception.D365ErrorCode);
        Assert.Equal(expectedOutcome, exception.MutationOutcome);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task Mutation_Second401ThrowsAuthenticationExceptionAfterOneRefresh()
    {
        var tokenProvider = new StubTokenProvider("stale", "fresh");
        var harness = CreateHarness<TestEntity>(tokenProvider: tokenProvider);
        harness.Handler.Enqueue(HttpStatusCode.Unauthorized);
        harness.Handler.Enqueue(
            HttpStatusCode.Unauthorized,
            "{\"error\":{\"code\":\"invalid_token\",\"message\":\"expired\"}}");

        var exception = await Assert.ThrowsAsync<D365AuthenticationException>(() =>
            harness.Query.AddAsync(new { Name = "value" }));

        Assert.Equal(D365MutationOutcome.Rejected, exception.MutationOutcome);
        Assert.Equal(1, tokenProvider.RefreshCount);
        Assert.Equal(2, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task Mutation_Actual401RefreshesAndRetriesOnce()
    {
        var tokenProvider = new StubTokenProvider("stale", "fresh");
        var harness = CreateHarness<TestEntity>(tokenProvider: tokenProvider);
        harness.Handler.Enqueue(HttpStatusCode.Unauthorized);
        harness.Handler.Enqueue(HttpStatusCode.NoContent);

        var response = await harness.Query.AddAsync(new { Name = "value" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, tokenProvider.RefreshCount);
        Assert.Equal(2, harness.Handler.Requests.Count);
        Assert.Equal("Bearer stale", Assert.Single(harness.Handler.Requests[0].Headers["Authorization"]));
        Assert.Equal("Bearer fresh", Assert.Single(harness.Handler.Requests[1].Headers["Authorization"]));
    }

    [Fact]
    public async Task TypedAddAsync_ReturnsParsedResponseWithDiagnostics()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":42,\"name\":\"created\"}")
            };
            response.Headers.TryAddWithoutValidation("x-ms-service-request-id", "request-created");
            return Task.FromResult(response);
        });

        var response = await harness.Query.AddAsync<CreatedEntity>(new { Name = "created" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var value = Assert.IsType<CreatedEntity>(response.Value);
        Assert.Equal(42, value.Id);
        Assert.Equal("created", value.Name);
        Assert.Equal("request-created", response.RequestId);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, response.MutationOutcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TypedAddAsync_EmptySuccessBodyThrowsProtocolException(string body)
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(HttpStatusCode.NoContent, body);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() =>
            harness.Query.AddAsync<CreatedEntity>(new { Name = "created" }));

        Assert.Equal(HttpStatusCode.NoContent, exception.StatusCode);
        Assert.Equal(HttpMethod.Post, exception.Method);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, exception.MutationOutcome);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task TypedAddAsync_NullSuccessValueThrowsProtocolException()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(HttpStatusCode.OK, "null");

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() =>
            harness.Query.AddAsync<CreatedEntity>(new { Name = "created" }));

        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, exception.MutationOutcome);
    }

    [Fact]
    public async Task TypedAddAsync_MalformedSuccessBodyThrowsSerializationException()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(HttpStatusCode.Created, "{not-json");

        var exception = await Assert.ThrowsAsync<D365SerializationException>(() =>
            harness.Query.AddAsync<CreatedEntity>(new { Name = "created" }));

        Assert.Equal(HttpStatusCode.Created, exception.StatusCode);
        Assert.Equal(HttpMethod.Post, exception.Method);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, exception.MutationOutcome);
        Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Mutation_TransportFailureIsUnknownAndNotRetried()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.EnqueueException(new HttpRequestException("network down"));

        var exception = await Assert.ThrowsAsync<D365TransportException>(() =>
            harness.Query.AddAsync(new { Name = "value" }));

        Assert.Equal(D365FailureKind.Transport, exception.FailureKind);
        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task Mutation_TimeoutIsUnknownAndNotRetried()
    {
        var options = CreateOptions();
        options.RequestTimeout = TimeSpan.FromMilliseconds(20);
        var harness = CreateHarness<TestEntity>(options);
        harness.Handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var exception = await Assert.ThrowsAsync<D365TransportException>(() =>
            harness.Query.AddAsync(new { Name = "value" }));

        Assert.Equal(D365FailureKind.Timeout, exception.FailureKind);
        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task Mutation_CallerCancellationAfterSendIsUnknownAndNotRetried()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<D365OperationCanceledException>(() =>
            harness.Query.AddAsync(new { Name = "value" }, cancellation.Token));

        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task Mutation_PreCanceledRequestIsNotSent()
    {
        var harness = CreateHarness<TestEntity>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<D365OperationCanceledException>(() =>
            harness.Query.AddAsync(new { Name = "value" }, cancellation.Token));

        Assert.Equal(D365MutationOutcome.NotSent, exception.MutationOutcome);
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task UpdateAsync_SupportsOdataKeyWhereAndAddIdentity()
    {
        var id = Guid.Parse("20136305-68d1-ef11-8ee9-000d3aa08849");
        var whereHarness = CreateHarness<KeyedEntity>();
        whereHarness.Handler.Enqueue(HttpStatusCode.NoContent);
        var identityHarness = CreateHarness<KeyedEntity>();
        identityHarness.Handler.Enqueue(HttpStatusCode.NoContent);

        await whereHarness.Query
            .Where(entity => entity.Id == id)
            .UpdateAsync(new { Name = "where" });
        await identityHarness.Query
            .AddIdentity("entityid", id)
            .UpdateAsync(new { Name = "identity" });

        var expectedSuffix = $"Entities(entityid={id})";
        Assert.EndsWith(expectedSuffix, Assert.Single(whereHarness.Handler.Requests).RequestUri!.ToString());
        Assert.EndsWith(expectedSuffix, Assert.Single(identityHarness.Handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task UpdateAsync_AnonymousKeysAcceptsCancellationToken()
    {
        var harness = CreateHarness<TestEntity>();
        harness.Handler.Enqueue(HttpStatusCode.NoContent);

        var response = await harness.Query.UpdateAsync(
            new { Id = 42 },
            new TestEntity { Name = "updated" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.EndsWith("Entities(Id=42)", Assert.Single(harness.Handler.Requests).RequestUri!.ToString());
    }

    private static MutationHarness<T> CreateHarness<T>(
        D365ClientOptions? options = null,
        StubTokenProvider? tokenProvider = null)
    {
        options ??= CreateOptions();
        tokenProvider ??= new StubTokenProvider("token");
        var handler = new StubHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.GetBaseUrl()),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(httpClient);
        var query = new D365Query<T>(
            factory.Object,
            NullLogger.Instance,
            tokenProvider,
            "Entities",
            options);

        return new MutationHarness<T>(query, handler);
    }

    private static D365ClientOptions CreateOptions()
    {
        return new D365ClientOptions
        {
            OrganizationUrl = "https://example.test/data/",
            RequestTimeout = TimeSpan.FromSeconds(5)
        };
    }

    private sealed record MutationHarness<T>(
        D365Query<T> Query,
        StubHttpMessageHandler Handler);

    private sealed class TestEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class KeyedEntity
    {
        [OdataKey]
        [JsonPropertyName("entityid")]
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CreatedEntity
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
