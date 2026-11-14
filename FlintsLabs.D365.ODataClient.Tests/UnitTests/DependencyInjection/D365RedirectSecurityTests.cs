using System.Net;
using System.Security.Cryptography.X509Certificates;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.DependencyInjection;

public class D365RedirectSecurityTests
{
    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task ApiPost_ReturnsRedirect_WithoutForwardingTokenOrPayload(int status)
    {
        await using var target = new LoopbackTlsServer();
        await using var source = new LoopbackTlsServer(statusCode: (HttpStatusCode)status, location: target.Address);
        using var provider = BuildProvider(source, target);
        var options = provider.GetRequiredService<IOptionsMonitor<D365ClientOptions>>().Get("Redirect");
        var transport = new D365Transport(provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger.Instance, new StubTokenProvider("synthetic-token"), options);

        var response = await transport.SendRawAsync(
            D365Request.Json(HttpMethod.Post, "Entities", "{\"private\":\"payload\"}", "Entities"), default);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Contains("Bearer synthetic-token", source.RequestText);
        Assert.Contains("{\"private\":\"payload\"}", source.RequestText);
        Assert.False(target.RequestReceived);
    }

    [Theory]
    [InlineData(307)]
    [InlineData(308)]
    public async Task AdfsPost_FailsOnRedirect_WithoutForwardingClientSecret(int status)
    {
        await using var target = new LoopbackTlsServer(body: "{\"access_token\":\"synthetic-token\"}");
        await using var source = new LoopbackTlsServer(statusCode: (HttpStatusCode)status, location: target.Address);
        using var provider = BuildProvider(source, target);
        var options = provider.GetRequiredService<IOptionsMonitor<D365ClientOptions>>().Get("Redirect");
        var tokens = new D365AccessTokenProvider(NullLogger<D365AccessTokenProvider>.Instance,
            provider.GetRequiredService<IHttpClientFactory>(), Options.Create(options));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => tokens.GetAccessTokenAsync().AsTask());

        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
        Assert.DoesNotContain("synthetic-secret", exception.ToString());
        Assert.Contains("client_secret=synthetic-secret", source.RequestText);
        Assert.False(target.RequestReceived);
    }

    [Fact]
    public async Task EnsuredApi_FailsOnRedirect_InsteadOfReportingSuccess()
    {
        await using var target = new LoopbackTlsServer();
        await using var source = new LoopbackTlsServer(statusCode: HttpStatusCode.TemporaryRedirect, location: target.Address);
        using var provider = BuildProvider(source, target);
        var options = provider.GetRequiredService<IOptionsMonitor<D365ClientOptions>>().Get("Redirect");
        var transport = new D365Transport(provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger.Instance, new StubTokenProvider("synthetic-token"), options);

        var exception = await Assert.ThrowsAsync<D365HttpException>(() =>
            transport.SendEnsuredAsync(D365Request.Get("Entities", "Entities"), default));

        Assert.Equal(HttpStatusCode.TemporaryRedirect, exception.StatusCode);
        Assert.False(target.RequestReceived);
    }

    [Fact]
    public async Task AdfsPost_AcceptsSuccessfulHttpsTokenResponse()
    {
        await using var source = new LoopbackTlsServer(body: "{\"access_token\":\"synthetic-token\",\"expires_in\":3600}");
        using var provider = BuildProvider(source);
        var options = provider.GetRequiredService<IOptionsMonitor<D365ClientOptions>>().Get("Redirect");
        var tokens = new D365AccessTokenProvider(NullLogger<D365AccessTokenProvider>.Instance,
            provider.GetRequiredService<IHttpClientFactory>(), Options.Create(options));

        var token = await tokens.GetAccessTokenAsync();

        Assert.Equal("synthetic-token", token.Value);
        Assert.Contains("POST /token HTTP/1.1", source.RequestText);
        Assert.Contains("client_secret=synthetic-secret", source.RequestText);
    }

    private static ServiceProvider BuildProvider(LoopbackTlsServer source, params LoopbackTlsServer[] targets)
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient("Redirect", b => b.UseADFS()
            .WithResource(source.Address.AbsoluteUri)
            .WithTokenEndpoint(new Uri(source.Address, "token").AbsoluteUri)
            .WithClientId("synthetic-client").WithClientSecret("synthetic-secret"));
        foreach (var name in new[] { "D365Endpoint_Redirect", "D365Auth_Redirect" })
        {
            services.AddHttpClient(name, c => c.Timeout = TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler((handler, _) =>
                {
                    // Preserve the package's handler/redirect/certificate settings; only trust test CAs.
                    var sockets = Assert.IsType<SocketsHttpHandler>(handler);
                    sockets.UseProxy = false;
                    var policy = new X509ChainPolicy
                    {
                        TrustMode = X509ChainTrustMode.CustomRootTrust,
                        RevocationMode = X509RevocationMode.NoCheck,
                        DisableCertificateDownloads = true
                    };
                    policy.CustomTrustStore.Add(source.RootCertificate);
                    foreach (var target in targets)
                        policy.CustomTrustStore.Add(target.RootCertificate);
                    sockets.SslOptions.CertificateChainPolicy = policy;
                });
        }
        return services.BuildServiceProvider();
    }
}
