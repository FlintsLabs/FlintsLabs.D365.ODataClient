using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;

internal static class D365QueryTestFactory
{
    public static D365Query<T> Create<T>(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        ID365AccessTokenProvider tokenProvider,
        string entity,
        D365ClientOptions options)
    {
        var transport = new D365Transport(
            httpClientFactory,
            logger,
            tokenProvider,
            options);
        return new D365Query<T>(logger, entity, options, transport);
    }

    public static D365Query<T> Create<T>(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        ID365AccessTokenProvider tokenProvider,
        string entity,
        D365ClientOptions options,
        ID365Transport transport)
    {
        return new D365Query<T>(logger, entity, options, transport);
    }
}
