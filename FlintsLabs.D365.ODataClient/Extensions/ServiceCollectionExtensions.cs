using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        string name,
        Action<D365ClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(D365ClientRegistration)
                && descriptor.ImplementationInstance is D365ClientRegistration registration
                && string.Equals(registration.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"D365 client '{name}' is already registered in this service collection. Use a unique name for each client.");
        }

        var builder = new D365ClientBuilder();
        configure(builder);
        if (builder.Options.Retry is null)
            throw new InvalidOperationException("D365 Retry options are not configured.");
        builder.Options.Retry.Validate();

        var snapshot = CloneOptions(builder.Options);
        snapshot.HttpClientName = $"D365Endpoint_{name}";
        snapshot.AuthHttpClientName = $"D365Auth_{name}";

        services.AddSingleton(new D365ClientRegistration(name));
        services.AddOptions<D365ClientOptions>(name)
            .Configure(options => CopyOptions(snapshot, options));
        services.AddLogging(logging =>
        {
            // The default HttpClientFactory Information logs include the full URL,
            // including OData filter values. Keep framework logs to status/failures.
            logging.AddFilter(
                $"System.Net.Http.HttpClient.{snapshot.HttpClientName}",
                LogLevel.Warning);
            logging.AddFilter(
                $"System.Net.Http.HttpClient.{snapshot.AuthHttpClientName}",
                LogLevel.Warning);
        });

        services.AddHttpClient(snapshot.HttpClientName, client =>
            {
                var baseUrl = snapshot.GetBaseUrl();
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    client.BaseAddress = new Uri(baseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(CreatePermissiveHandler);

        services.AddHttpClient(snapshot.AuthHttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(CreatePermissiveHandler);

        services.TryAddSingleton<ID365ClientFactory, D365ClientFactory>();
        services.TryAddSingleton<ID365Client>(serviceProvider =>
            serviceProvider.GetRequiredService<ID365ClientFactory>().GetClient());

        return services;
    }

    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        Action<D365ClientBuilder> configure)
    {
        return services.AddD365ODataClient("Default", configure);
    }

    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string sectionName)
    {
        return services.AddD365ODataClient(name, builder =>
            builder.FromConfiguration(configuration, sectionName));
    }

    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        D365ServiceScope scope,
        Action<D365ClientBuilder> configure)
    {
        return services.AddD365ODataClient(scope.ToString(), configure);
    }

    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        D365ServiceScope scope,
        IConfiguration configuration,
        string sectionName)
    {
        return services.AddD365ODataClient(
            scope.ToString(),
            configuration,
            sectionName);
    }

    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "D365")
    {
        return services.AddD365ODataClient(
            "Default",
            configuration,
            sectionName);
    }

    private static HttpMessageHandler CreatePermissiveHandler()
    {
        // Keep the existing TLS behavior for v2.0.0; see the security documentation.
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
    }

    private static D365ClientOptions CloneOptions(D365ClientOptions source)
    {
        var clone = new D365ClientOptions();
        CopyOptions(source, clone);
        return clone;
    }

    private static void CopyOptions(D365ClientOptions source, D365ClientOptions destination)
    {
        destination.RequestTimeout = source.RequestTimeout;
        destination.MaxErrorBodyBytes = source.MaxErrorBodyBytes;
        destination.MaxPages = source.MaxPages;
        destination.Retry = new D365RetryOptions
        {
            MaxReadRetries = source.Retry.MaxReadRetries,
            BaseDelay = source.Retry.BaseDelay,
            MaxDelay = source.Retry.MaxDelay,
            UseJitter = source.Retry.UseJitter
        };
        destination.AuthType = source.AuthType;
        destination.ManagedIdentityClientId = source.ManagedIdentityClientId;
        destination.BooleanFormatting = source.BooleanFormatting;
        destination.Scope = source.Scope;
        destination.HttpClientName = source.HttpClientName;
        destination.AuthHttpClientName = source.AuthHttpClientName;
        destination.ClientId = source.ClientId;
        destination.ClientSecret = source.ClientSecret;
        destination.TenantId = source.TenantId;
        destination.Resource = source.Resource;
        destination.OrganizationUrl = source.OrganizationUrl;
        destination.TokenEndpoint = source.TokenEndpoint;
        destination.GrantType = source.GrantType;
    }
}
