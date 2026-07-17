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

public class D365CountTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    public async Task CountAndLongCount_ValidCount_ReturnExpectedValue(long expected)
    {
        var longTransport = new CountTransport();
        longTransport.Enqueue(HttpStatusCode.OK, CountBody(expected));
        var intTransport = new CountTransport();
        intTransport.Enqueue(HttpStatusCode.OK, CountBody(expected));

        var longCount = await CreateQuery(longTransport).LongCountAsync();
        var count = await CreateQuery(intTransport).CountAsync();

        Assert.Equal(expected, longCount);
        Assert.Equal((int)expected, count);
        Assert.Contains("$count=true", Assert.Single(longTransport.Requests).RelativeOrAbsoluteUrl);
        Assert.Contains("$top=0", Assert.Single(longTransport.Requests).RelativeOrAbsoluteUrl);
    }

    [Fact]
    public async Task Count_Int32Overflow_ThrowsWhileLongCountSucceeds()
    {
        const long expected = (long)int.MaxValue + 1;
        var longTransport = new CountTransport();
        longTransport.Enqueue(HttpStatusCode.OK, CountBody(expected));
        var intTransport = new CountTransport();
        intTransport.Enqueue(HttpStatusCode.OK, CountBody(expected));

        Assert.Equal(expected, await CreateQuery(longTransport).LongCountAsync());
        await Assert.ThrowsAsync<OverflowException>(() => CreateQuery(intTransport).CountAsync());
    }

    [Fact]
    public async Task LongCount_MissingCount_ThrowsProtocolException()
    {
        var transport = new CountTransport();
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() =>
            CreateQuery(transport).LongCountAsync());

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("request-count", exception.RequestId);
    }

    [Theory]
    [InlineData("\"42\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    public async Task LongCount_InvalidCount_ThrowsProtocolException(string countJson)
    {
        var transport = new CountTransport();
        transport.Enqueue(HttpStatusCode.OK, $"{{\"value\":[],\"@odata.count\":{countJson}}}");

        await Assert.ThrowsAsync<D365ProtocolException>(() =>
            CreateQuery(transport).LongCountAsync());
    }

    [Fact]
    public async Task LongCount_NonSuccessResponse_ThrowsHttpException()
    {
        var transport = new CountTransport();
        transport.Enqueue(HttpStatusCode.ServiceUnavailable, "unavailable");

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            CreateQuery(transport).LongCountAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task LongCount_TransportFailure_Propagates()
    {
        var expected = new D365TransportException("network down");
        var transport = new CountTransport();
        transport.Enqueue(expected);

        var exception = await Assert.ThrowsAsync<D365TransportException>(() =>
            CreateQuery(transport).LongCountAsync());

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task LongCount_ClientFilterScansAllPages()
    {
        var transport = new CountTransport();
        transport.Enqueue(
            HttpStatusCode.OK,
            "{\"value\":[{\"Active\":true},{\"Active\":false}]," +
            "\"@odata.nextLink\":\"Entities?$skiptoken=second\"}");
        transport.Enqueue(HttpStatusCode.OK, "{\"value\":[{\"Active\":true}]}");
        var query = CreateQuery(transport).WhereClient(entity => entity.Active);

        var count = await query.LongCountAsync();

        Assert.Equal(2, count);
        Assert.Equal(2, transport.Requests.Count);
        Assert.DoesNotContain("$top=0", transport.Requests[0].RelativeOrAbsoluteUrl);
    }

    [Fact]
    public async Task LongCount_ClientFilterSecondPageFailure_ThrowsWithPartialDiagnostics()
    {
        var transport = new CountTransport();
        transport.Enqueue(
            HttpStatusCode.OK,
            "{\"value\":[{\"Active\":true},{\"Active\":false}]," +
            "\"@odata.nextLink\":\"Entities?$skiptoken=second\"}");
        transport.Enqueue(HttpStatusCode.InternalServerError, "failed");
        var query = CreateQuery(transport).WhereClient(entity => entity.Active);

        var exception = await Assert.ThrowsAsync<D365HttpException>(() => query.LongCountAsync());

        Assert.Equal(1, exception.PartialRecordCount);
    }

    private static string CountBody(long count)
    {
        return $"{{\"value\":[],\"@odata.count\":{count}}}";
    }

    private static D365Query<TestEntity> CreateQuery(ID365Transport transport)
    {
        return D365QueryTestFactory.Create<TestEntity>(
            Mock.Of<IHttpClientFactory>(),
            NullLogger.Instance,
            new StubTokenProvider("token"),
            "Entities",
            new D365ClientOptions
            {
                OrganizationUrl = "https://example.test/data/",
                MaxPages = 10
            },
            transport);
    }

    private sealed class TestEntity
    {
        public bool Active { get; set; }
    }

    private sealed class CountTransport : ID365Transport
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
                "request-count",
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
                throw new InvalidOperationException("No queued count response.");

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
