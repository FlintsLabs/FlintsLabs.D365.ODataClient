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

public class D365PaginationTests
{
    [Theory]
    [InlineData("Entities?$skiptoken=second")]
    [InlineData("/data/Entities?$skiptoken=second")]
    [InlineData("https://example.test/data/Entities?$skiptoken=second")]
    public async Task ToList_ValidRelativeOrAbsoluteNextLink_ReturnsAllPages(string nextLink)
    {
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, nextLink));
        transport.Enqueue(HttpStatusCode.OK, Page(2));
        var query = CreateQuery(transport);

        var records = await query.ToListAsync();

        Assert.Equal(new[] { 1, 2 }, records.Select(record => record.Id));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(
            "https://example.test/data/Entities?$skiptoken=second",
            transport.Requests[1].RelativeOrAbsoluteUrl);
    }

    [Fact]
    public async Task ToList_SecondPageHttpFailure_ThrowsWithoutReturningPartialRecords()
    {
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, "Entities?$skiptoken=second"));
        transport.Enqueue(HttpStatusCode.InternalServerError, "server failed");
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365HttpException>(() => query.ToListAsync());

        Assert.Equal(1, exception.PartialRecordCount);
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example.test/data/Entities?$skiptoken=second")]
    [InlineData("https://example.test/api/data/v9.2/Entities?$skiptoken=second")]
    [InlineData("ftp://example.test/data/Entities")]
    [InlineData("https://user@example.test/data/Entities")]
    [InlineData("http://[")]
    public async Task ToList_UnsafeNextLink_ThrowsBeforeSendingSecondRequest(string nextLink)
    {
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, nextLink));
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() => query.ToListAsync());

        Assert.Equal(1, exception.PartialRecordCount);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ToList_RepeatedNextLink_ThrowsLoopError()
    {
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, "https://example.test/data/Entities"));
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() => query.ToListAsync());

        Assert.Contains("loop", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, exception.PartialRecordCount);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ToList_MaxPages_ThrowsBeforeFetchingExcessPage()
    {
        var options = CreateOptions();
        options.MaxPages = 1;
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, "Entities?$skiptoken=second"));
        var query = CreateQuery(transport, options);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() => query.ToListAsync());

        Assert.Contains("MaxPages", exception.Message);
        Assert.Equal(1, exception.PartialRecordCount);
        Assert.Single(transport.Requests);
    }

    [Fact]
    public async Task ToList_SecondPageCancellation_PropagatesWithoutReturningPartialRecords()
    {
        var cancellation = new CancellationToken(true);
        var expected = new D365OperationCanceledException(
            "canceled",
            D365MutationOutcome.NotApplicable,
            cancellation);
        var transport = new PagingTransport();
        transport.Enqueue(HttpStatusCode.OK, Page(1, "Entities?$skiptoken=second"));
        transport.Enqueue(expected);
        var query = CreateQuery(transport);

        var exception = await Assert.ThrowsAsync<D365OperationCanceledException>(() => query.ToListAsync());

        Assert.Same(expected, exception);
        Assert.Equal(2, transport.Requests.Count);
    }

    private static string Page(int id, string? nextLink = null)
    {
        var nextLinkJson = nextLink is null
            ? string.Empty
            : $",\"@odata.nextLink\":{System.Text.Json.JsonSerializer.Serialize(nextLink)}";
        return $"{{\"value\":[{{\"Id\":{id}}}]{nextLinkJson}}}";
    }

    private static D365Query<TestEntity> CreateQuery(
        ID365Transport transport,
        D365ClientOptions? options = null)
    {
        return new D365Query<TestEntity>(
            Mock.Of<IHttpClientFactory>(),
            NullLogger.Instance,
            new StubTokenProvider("token"),
            "Entities",
            options ?? CreateOptions(),
            transport);
    }

    private static D365ClientOptions CreateOptions()
    {
        return new D365ClientOptions
        {
            OrganizationUrl = "https://example.test/data/",
            MaxPages = 10
        };
    }

    private sealed class TestEntity
    {
        public int Id { get; set; }
    }

    private sealed class PagingTransport : ID365Transport
    {
        private readonly Queue<Func<D365Request, D365Response>> _steps = new();

        public List<D365Request> Requests { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            _steps.Enqueue(request => new D365Response(
                statusCode,
                body,
                new Dictionary<string, string[]>(),
                Resolve(request.RelativeOrAbsoluteUrl),
                "request-1",
                D365MutationOutcome.NotApplicable));
        }

        public void Enqueue(Exception exception)
        {
            _steps.Enqueue(_ => throw exception);
        }

        public Task<D365Response> SendRawAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            return SendAsync(request, ensureSuccess: false);
        }

        public Task<D365Response> SendEnsuredAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            return SendAsync(request, ensureSuccess: true);
        }

        private Task<D365Response> SendAsync(D365Request request, bool ensureSuccess)
        {
            Requests.Add(request);
            if (_steps.Count == 0)
                throw new InvalidOperationException("No queued page response.");

            var response = _steps.Dequeue()(request);
            if (ensureSuccess)
                response.EnsureSuccessStatusCode();
            return Task.FromResult(response);
        }

        private static Uri Resolve(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(new Uri("https://example.test/data/"), url);
        }
    }
}
