using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Queries;

public class D365ReadBehaviorTests
{
    [Fact]
    public async Task ToList_Valid200_ReturnsRecords()
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[{\"Id\":7,\"Name\":\"A\"}]}");
        var query = CreateQuery(transport);

        var records = await query.ToListAsync();

        var record = Assert.Single(records);
        Assert.Equal(7, record.Id);
        Assert.Equal("A", record.Name);
    }

    [Fact]
    public async Task FirstOrDefault_ValidEmpty200_ReturnsNull()
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");
        var query = CreateQuery(transport);

        var record = await query.FirstOrDefaultAsync();

        Assert.Null(record);
        Assert.Contains("$top=1", Assert.Single(transport.Requests).RelativeOrAbsoluteUrl);
    }

    [Fact]
    public async Task FirstOrDefault_NonSuccessResponse_ThrowsInsteadOfReturningNull()
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.InternalServerError, "server failed");
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            query.FirstOrDefaultAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ToList_NonSuccessHttpResponse_Throws(HttpStatusCode statusCode)
    {
        var transport = new QueryTransport();
        transport.Enqueue(statusCode, "{\"error\":{\"message\":\"failed\"}}");
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365HttpException>(() => query.ToListAsync());

        Assert.Equal(statusCode, exception.StatusCode);
    }

    [Theory]
    [MemberData(nameof(NonHttpFailures))]
    public async Task ToList_NonHttpFailure_PropagatesWithoutReturningEmptyList(Exception failure)
    {
        var transport = new QueryTransport();
        transport.Enqueue(failure);
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => query.ToListAsync());

        Assert.Same(failure, exception);
    }

    public static TheoryData<Exception> NonHttpFailures => new()
    {
        new D365TransportException("network down"),
        new D365TransportException("timed out", D365FailureKind.Timeout),
        new D365OperationCanceledException(
            "canceled",
            D365MutationOutcome.NotApplicable,
            new CancellationToken(true))
    };

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    public async Task ToList_MalformedJson_ThrowsSerializationException(string body)
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, body);
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365SerializationException>(() => query.ToListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("request-1", exception.RequestId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":{}}")]
    [InlineData("[]")]
    [InlineData("{\"error\":{\"code\":\"bad\",\"message\":\"logical failure\"}}")]
    public async Task ToList_InvalidSuccessfulEnvelope_ThrowsProtocolException(string body)
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, body);
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() => query.ToListAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("request-1", exception.RequestId);
    }

    [Fact]
    public async Task ToList_RecordDeserializationFailure_ThrowsSerializationException()
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[{\"Id\":\"not-a-number\"}]}");
        var query = CreateQuery(transport);

        await Assert.ThrowsAsync<D365SerializationException>(() => query.ToListAsync());
    }

    [Fact]
    public async Task ToList_PassesHeadersAndCancellationToTransport()
    {
        var transport = new QueryTransport();
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");
        var query = CreateQuery(transport).AddHeader("Prefer", "odata.maxpagesize=25");
        using var cancellation = new CancellationTokenSource();

        await query.ToListAsync(cancellation.Token);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("odata.maxpagesize=25", request.Headers["Prefer"]);
        Assert.Equal(cancellation.Token, transport.CancellationTokens.Single());
    }

    private static D365Query<TestEntity> CreateQuery(ID365Transport transport)
    {
        return new D365Query<TestEntity>(
            Mock.Of<IHttpClientFactory>(),
            NullLogger.Instance,
            new StubTokenProvider("token"),
            "Entities",
            new D365ClientOptions { Resource = "https://example.test" },
            transport);
    }

    private sealed class TestEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class QueryTransport : ID365Transport
    {
        private readonly Queue<Func<D365Response>> _steps = new();

        public List<D365Request> Requests { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            _steps.Enqueue(() => new D365Response(
                statusCode,
                body,
                new Dictionary<string, string[]>(),
                new Uri("https://example.test/data/Entities"),
                "request-1",
                D365MutationOutcome.NotApplicable));
        }

        public void Enqueue(Exception exception)
        {
            _steps.Enqueue(() => throw exception);
        }

        public Task<D365Response> SendRawAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken, ensureSuccess: false);
        }

        public Task<D365Response> SendEnsuredAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            return SendAsync(request, cancellationToken, ensureSuccess: true);
        }

        private Task<D365Response> SendAsync(
            D365Request request,
            CancellationToken cancellationToken,
            bool ensureSuccess)
        {
            Requests.Add(request);
            CancellationTokens.Add(cancellationToken);
            if (_steps.Count == 0)
                throw new InvalidOperationException("No queued query response.");

            var response = _steps.Dequeue()();
            if (ensureSuccess)
                response.EnsureSuccessStatusCode();
            return Task.FromResult(response);
        }
    }
}
