using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FlintsLabs.D365.ODataClient.Extensions;

/// <summary>
/// Extension methods for registering D365 OData client services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add D365 OData client services to the dependency injection container
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Configuration action for D365 client options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        Action<D365ClientOptions> configure)
    {
        // Configure options
        services.Configure(configure);
        
        // Register HTTP client factory with named client
        services.AddHttpClient("D365Endpoint", (serviceProvider, client) =>
        {
            var options = new D365ClientOptions();
            configure(options);
            
            if (!string.IsNullOrWhiteSpace(options.Resource))
            {
                var baseUrl = options.Resource.TrimEnd('/') + "/data/";
                client.BaseAddress = new Uri(baseUrl);
            }
        });
        
        // Register token provider as singleton (caches tokens)
        services.AddSingleton<ID365AccessTokenProvider, D365AccessTokenProvider>();
        
        // Register D365 service as scoped (one instance per request)
        services.AddScoped<ID365Service, D365Service>();
        
        return services;
    }
    
    /// <summary>
    /// Add D365 OData client services with configuration from IConfiguration section
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configurationSection">Configuration section name (default: "D365")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "D365")
    {
        var section = configuration.GetSection(sectionName);
        
        services.Configure<D365ClientOptions>(section);
        
        // Register HTTP client factory with named client
        services.AddHttpClient("D365Endpoint", (serviceProvider, client) =>
        {
            var resource = section["Resource"];
            
            if (!string.IsNullOrWhiteSpace(resource))
            {
                var baseUrl = resource.TrimEnd('/') + "/data/";
                client.BaseAddress = new Uri(baseUrl);
            }
        });
        
        // Register token provider as singleton (caches tokens)
        services.AddSingleton<ID365AccessTokenProvider, D365AccessTokenProvider>();
        
        // Register D365 service as scoped (one instance per request)
        services.AddScoped<ID365Service, D365Service>();
        
        return services;
    }
}
