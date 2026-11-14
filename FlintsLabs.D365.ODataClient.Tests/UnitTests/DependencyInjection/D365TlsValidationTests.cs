using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.DependencyInjection;

public class D365TlsValidationTests
{
    [Theory]
    [InlineData("Host")]
    [InlineData("hOsT")]
    public async Task Query_RejectsHostOverride_BeforeTokenOrTlsCanTargetAnotherVirtualHost(string header)
    {
        await using var server = new LoopbackTlsServer(wrongHost: true);
        using var provider = BuildProvider(server, "D365Endpoint_Tls", trustTestRoot: true);
        var options = provider.GetRequiredService<IOptionsMonitor<D365ClientOptions>>().Get("Tls");
        var tokens = new StubTokenProvider("synthetic-token");
        var transport = new D365Transport(provider.GetRequiredService<IHttpClientFactory>(),
            NullLogger.Instance, tokens, options);
        var client = new D365Client(NullLogger.Instance, options, transport);

        var exception = await Assert.ThrowsAsync<D365ProtocolException>(() => client
            .Entity<object>("Entities")
            .AddHeader(header, "wrong.example.test")
            .AddAsync(new { Secret = "synthetic-payload" }));

        Assert.Equal(D365MutationOutcome.NotSent, exception.MutationOutcome);
        Assert.Equal(0, tokens.GetCount);
        Assert.False(server.RequestReceived);
    }

    [Theory]
    [InlineData("D365Endpoint_Tls")]
    [InlineData("D365Auth_Tls")]
    public async Task NamedClient_RejectsUntrustedCertificate_BeforeSendingHttpRequest(string clientName)
    {
        await using var server = new LoopbackTlsServer();
        using var provider = BuildProvider(server, clientName, trustTestRoot: false);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(server.Address));

        Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
        Assert.IsAssignableFrom<AuthenticationException>(exception.InnerException);
        await server.Completion;
        Assert.False(server.RequestReceived);
    }

    [Theory]
    [InlineData("D365Endpoint_Tls")]
    [InlineData("D365Auth_Tls")]
    public async Task NamedClient_AcceptsTrustedCertificate_WithMatchingHostAndValidDates(string clientName)
    {
        await using var server = new LoopbackTlsServer();
        using var provider = BuildProvider(server, clientName, trustTestRoot: true);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        using var response = await client.GetAsync(server.Address);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
        await server.Completion;
        Assert.True(server.RequestReceived);
    }

    [Theory]
    [InlineData("D365Endpoint_Tls", true, false)]
    [InlineData("D365Auth_Tls", true, false)]
    [InlineData("D365Endpoint_Tls", false, true)]
    [InlineData("D365Auth_Tls", false, true)]
    public async Task NamedClient_RejectsInvalidCertificate_EvenWithTrustedIssuer(
        string clientName, bool wrongHost, bool expired)
    {
        await using var server = new LoopbackTlsServer(wrongHost, expired);
        using var provider = BuildProvider(server, clientName, trustTestRoot: true);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(server.Address));

        Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
        Assert.IsAssignableFrom<AuthenticationException>(exception.InnerException);
        await server.Completion;
        Assert.False(server.RequestReceived);
    }

    private static ServiceProvider BuildProvider(
        LoopbackTlsServer server, string clientName, bool trustTestRoot)
    {
        var services = new ServiceCollection();
        services.AddD365ODataClient("Tls", builder => builder.WithResource(server.Address.ToString()));
        services.AddHttpClient(clientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler((handler, _) =>
            {
                // Modify the registered handler in place, never replace it or its validation callback.
                // Microsoft.Extensions.Http 9 uses SocketsHttpHandler on both target frameworks.
                if (handler is SocketsHttpHandler sockets)
                {
                    sockets.UseProxy = false;
                    if (trustTestRoot)
                    {
                        sockets.SslOptions.CertificateChainPolicy = new X509ChainPolicy
                        {
                            TrustMode = X509ChainTrustMode.CustomRootTrust,
                            CustomTrustStore = { server.RootCertificate },
                            RevocationMode = X509RevocationMode.NoCheck,
                            DisableCertificateDownloads = true
                        };
                    }
                }
                else
                {
                    // Exercise the legacy registration unchanged so the bypass is reproducible.
                    Assert.IsType<HttpClientHandler>(handler).UseProxy = false;
                }
            });
        return services.BuildServiceProvider();
    }
}
