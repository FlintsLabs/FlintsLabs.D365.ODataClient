using System.Net;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.DependencyInjection;

public class D365RegistrationTests
{
    [Fact]
    public void SameClientName_CanBeRegisteredInSeparateServiceProviders()
    {
        using var firstProvider = BuildProvider("Cloud", "https://first.example.test");
        using var secondProvider = BuildProvider("Cloud", "https://second.example.test");

        var firstFactory = firstProvider.GetRequiredService<ID365ClientFactory>();
        var secondFactory = secondProvider.GetRequiredService<ID365ClientFactory>();

        Assert.NotSame(firstFactory.GetClient("Cloud"), secondFactory.GetClient("Cloud"));
        Assert.Equal(
            "https://first.example.test/data/",
            firstProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("D365Endpoint_Cloud").BaseAddress?.ToString());
        Assert.Equal(
            "https://second.example.test/data/",
            secondProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("D365Endpoint_Cloud").BaseAddress?.ToString());
    }

    [Fact]
    public void DuplicateClientName_InSameServiceCollection_IsRejected()
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient("Cloud", builder =>
            builder.WithResource("https://first.example.test"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddD365ODataClient("Cloud", builder =>
                builder.WithResource("https://second.example.test")));

        Assert.Contains("Cloud", exception.Message);
    }

    [Fact]
    public void Factory_DefaultPrefersExplicitDefaultRegistration()
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient("Cloud", builder =>
            builder.WithResource("https://cloud.example.test"));
        services.AddD365ODataClient(builder =>
            builder.WithResource("https://default.example.test"));
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ID365ClientFactory>();

        Assert.Same(factory.GetClient("Default"), factory.GetClient());
        Assert.Same(factory.GetClient(), provider.GetRequiredService<ID365Client>());
    }

    [Fact]
    public void NamedHttpClient_UsesInfiniteTimeout()
    {
        using var provider = BuildProvider("Cloud", "https://cloud.example.test");

        var httpClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient("D365Endpoint_Cloud");

        Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
    }

    [Fact]
    public void NamedHttpClient_FrameworkLogsAreSuppressedBelowWarning()
    {
        using var provider = BuildProvider("Cloud", "https://cloud.example.test");

        var rules = provider.GetRequiredService<IOptions<LoggerFilterOptions>>()
            .Value.Rules;

        Assert.Contains(rules, rule =>
            rule.CategoryName == "System.Net.Http.HttpClient.D365Endpoint_Cloud"
            && rule.LogLevel == LogLevel.Warning);
        Assert.Contains(rules, rule =>
            rule.CategoryName == "System.Net.Http.HttpClient.D365Auth_Cloud"
            && rule.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void Entity_ReturnsFreshQueryBuilders()
    {
        using var provider = BuildProvider("Cloud", "https://cloud.example.test");
        var client = provider.GetRequiredService<ID365ClientFactory>().GetClient("Cloud");

        var first = client.Entity<TestEntity>("Entities");
        var second = client.Entity<TestEntity>("Entities");

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task RawSend_SerializesPayloadOnceAndDelegatesToTransport()
    {
        var expectedResponse = new D365Response(
            HttpStatusCode.BadRequest,
            "bad request",
            new Dictionary<string, string[]>(),
            new Uri("https://example.test/data/Entities(1)"),
            "request-1",
            D365MutationOutcome.Rejected);
        var transport = new CapturingTransport(expectedResponse);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var options = new D365ClientOptions
        {
            Resource = "https://example.test",
            HttpClientName = "D365Endpoint_Test"
        };
        var client = new D365Client(
            NullLogger.Instance,
            options,
            transport);
        using var cancellation = new CancellationTokenSource();

        var response = await client.SendAsync(
            HttpMethod.Patch,
            "Entities(1)",
            new { name = "updated" },
            cancellation.Token);

        Assert.Same(expectedResponse, response);
        Assert.Equal(HttpMethod.Patch, transport.Request?.Method);
        Assert.Equal("Entities(1)", transport.Request?.RelativeOrAbsoluteUrl);
        Assert.Equal("{\"name\":\"updated\"}", transport.Request?.JsonPayload);
        Assert.Equal(cancellation.Token, transport.CancellationToken);
    }

    private static ServiceProvider BuildProvider(string name, string resource)
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient(name, builder =>
            builder.WithResource(resource));
        return services.BuildServiceProvider();
    }

    private sealed class TestEntity;

    private sealed class CapturingTransport(D365Response response) : ID365Transport
    {
        public D365Request? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<D365Response> SendRawAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(response);
        }

        public Task<D365Response> SendEnsuredAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
