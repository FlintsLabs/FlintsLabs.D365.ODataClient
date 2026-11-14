using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Transport;

public class D365EndpointSecurityTests
{
    [Theory]
    [InlineData("http://example.test/data/Entities")]
    [InlineData("https://other.test/data/Entities")]
    [InlineData("https://example.test:444/data/Entities")]
    [InlineData("//other.test/data/Entities")]
    [InlineData("\\\\other.test\\data\\Entities")]
    [InlineData("https://example.test@other.test/data/Entities")]
    [InlineData("https://user:secret@example.test/data/Entities")]
    [InlineData("Entities#private-fragment")]
    [InlineData("ftp://example.test/data/Entities")]
    [InlineData("https://[invalid/?secret=value")]
    public async Task RawRequest_RejectsUnsafeUri_BeforeAcquiringTokenOrSending(string url)
    {
        using var context = new RequestContext();

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() =>
            context.Client.SendAsync(HttpMethod.Post, url, new { secret = "payload" }));

        Assert.Equal(D365FailureKind.Protocol, exception.FailureKind);
        Assert.Equal(D365MutationOutcome.NotSent, exception.MutationOutcome);
        Assert.False(exception.IsTransient);
        Assert.Null(exception.RequestUri);
        Assert.DoesNotContain(url, exception.ToString());
        Assert.Equal(0, context.Tokens.GetCount);
        Assert.Empty(context.Handler.Requests);
    }

    [Theory]
    [InlineData("Entities?$top=1", "https://example.test/data/Entities?$top=1")]
    [InlineData("/data/Entities", "https://example.test/data/Entities")]
    [InlineData("//example.test/data/Entities", "https://example.test/data/Entities")]
    [InlineData("https://EXAMPLE.test:443/data/Entities", "https://example.test/data/Entities")]
    [InlineData("/api/services/CustomService/Read", "https://example.test/api/services/CustomService/Read")]
    public async Task RawRequest_SendsValidatedAbsoluteUri_ToConfiguredOrigin(string url, string expected)
    {
        // A later named-HttpClient customization must not redirect a relative request's token.
        using var context = new RequestContext("https://different.test/overridden/");

        var response = await context.Client.SendAsync(HttpMethod.Get, url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(context.Handler.Requests);
        Assert.Equal(expected, sent.RequestUri!.AbsoluteUri);
        Assert.Equal(sent.RequestUri, response.RequestUri);
        Assert.Equal("Bearer synthetic-token", Assert.Single(sent.Headers["Authorization"]));
    }

    [Fact]
    public async Task QueryRequest_RejectsForeignEntityUrl_BeforeAcquiringToken()
    {
        using var context = new RequestContext();

        await Assert.ThrowsAsync<D365ProtocolException>(() =>
            context.Client.Entity<object>("https://other.test/data/Entities").ToListAsync());

        Assert.Equal(0, context.Tokens.GetCount);
        Assert.Empty(context.Handler.Requests);
    }

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("relative/path")]
    [InlineData("https://user:secret@example.test")]
    [InlineData("https://example.test?secret=value")]
    [InlineData("https://example.test#fragment")]
    public void Registration_RejectsUnsafeApiEndpoint_ForFluentAndConfiguration(string endpoint)
    {
        var services = new ServiceCollection();
        var fluent = Assert.Throws<InvalidOperationException>(() =>
            services.AddD365ODataClient("Unsafe", b => b.WithResource(endpoint)));
        Assert.DoesNotContain(endpoint, fluent.ToString());

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["D365:AuthType"] = "ManagedIdentity",
            ["D365:Resource"] = endpoint
        }).Build();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddD365ODataClient("UnsafeConfig", configuration, "D365"));
        Assert.Throws<InvalidOperationException>(() =>
            services.AddD365ODataClient("UnsafeOrganization", b => b
                .WithResource("https://example.test").WithOrganizationUrl(endpoint)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://auth.test/token")]
    [InlineData("relative/token")]
    [InlineData("https://user:secret@auth.test/token")]
    [InlineData("https://auth.test/token#secret")]
    public void Adfs_RejectsUnsafeTokenEndpoint_AtRegistration(string? endpoint)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddD365ODataClient("Adfs", b => b
            .UseADFS().WithResource("https://example.test").WithTokenEndpoint(endpoint!)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://auth.test/token")]
    [InlineData("relative/token")]
    [InlineData("https://user:secret@auth.test/token")]
    [InlineData("https://auth.test/token#secret")]
    public async Task Adfs_RejectsUnsafeTokenEndpoint_BeforePost_WhenRegistrationIsBypassed(string? endpoint)
    {
        using var handler = new StubHttpMessageHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"synthetic-token\"}");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://auth.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        var provider = new D365AccessTokenProvider(NullLogger<D365AccessTokenProvider>.Instance,
            factory.Object, Options.Create(new D365ClientOptions
            {
                AuthType = D365AuthType.ADFS, TokenEndpoint = endpoint, ClientSecret = "synthetic-secret"
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync().AsTask());
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://other.test")]
    [InlineData("http://example.test")]
    public async Task RawRequest_RejectsUnsafeUrl_EvenWhenNamedHttpClientHasMatchingBaseAddress(string endpoint)
    {
        using var context = new RequestContext(endpoint + "/data/");

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() =>
            context.Client.SendAsync(HttpMethod.Get, endpoint + "/data/Entities"));

        Assert.Equal(D365MutationOutcome.NotApplicable, exception.MutationOutcome);
        Assert.Equal(0, context.Tokens.GetCount);
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task RuntimeOptions_RejectHttpApiEndpoint_BeforeTokenAcquisition()
    {
        using var context = new RequestContext();
        context.Options.Resource = "http://example.test";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Client.SendAsync(HttpMethod.Get, "Entities"));

        Assert.Equal(0, context.Tokens.GetCount);
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public void Registration_RejectsMissingApiEndpoint()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddD365ODataClient(b => b.UseSystemAssignedManagedIdentity()));
    }

    [Theory]
    [InlineData("https://b\u00fccher.example", "https://xn--bcher-kva.example/data/Entities")]
    [InlineData("https://[::1]:8443", "https://[::1]:8443/data/Entities")]
    public async Task RawRequest_AllowsEquivalentIdnAndIpv6Origins(string resource, string url)
    {
        using var context = new RequestContext();
        context.Options.Resource = resource;

        var response = await context.Client.SendAsync(HttpMethod.Get, url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(url, Assert.Single(context.Handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public void Adfs_AllowsSeparateHttpsAuthority_AndNonUrlAudienceWithOrganizationUrl()
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient("Adfs", b => b.UseADFS()
            .WithResource("urn:example:d365")
            .WithOrganizationUrl("https://example.test/namespaces/AXSF/")
            .WithTokenEndpoint("https://auth.test/adfs/oauth2/token"));
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("D365Endpoint_Adfs");
        Assert.Equal("https://example.test/namespaces/AXSF/data/", client.BaseAddress!.AbsoluteUri);
    }

    private sealed class RequestContext : IDisposable
    {
        public StubHttpMessageHandler Handler { get; } = new();
        public StubTokenProvider Tokens { get; } = new("synthetic-token");
        public D365ClientOptions Options { get; } = new() { Resource = "https://example.test" };
        public ID365Client Client { get; }
        private readonly HttpClient _httpClient;

        public RequestContext(string baseAddress = "https://example.test/data/")
        {
            Handler.Enqueue(HttpStatusCode.OK, "{\"value\":[]}");
            _httpClient = new HttpClient(Handler) { BaseAddress = new Uri(baseAddress) };
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
            var transport = new D365Transport(factory.Object, NullLogger.Instance, Tokens, Options);
            Client = new D365Client(NullLogger.Instance, Options, transport);
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
