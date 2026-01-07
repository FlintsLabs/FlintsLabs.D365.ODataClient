using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

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
        
        // Get options for HttpClient setup
        var options = new D365ClientOptions();
        configure(options);
        
        // Register HTTP client factory with named client
        services.AddHttpClient("D365Endpoint", (serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(options.GetBaseUrl());
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Support self-signed certificates for on-premise D365
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
        
        // Register unified token provider (handles both Azure AD and ADFS)
        services.AddSingleton<ID365AccessTokenProvider, D365AccessTokenProvider>();
        
        // Register D365 service as scoped (one instance per request)
        services.AddScoped<ID365Service, D365Service>();
        
        return services;
    }
    
    /// <summary>
    /// Add D365 OData client services with configuration from IConfiguration section
    /// Auto-detects Azure AD or ADFS based on TenantId/TokenEndpoint
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration</param>
    /// <param name="sectionName">Configuration section name (default: "D365")</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "D365")
    {
        var section = configuration.GetSection(sectionName);
        
        // Configure options with auto-detection
        services.Configure<D365ClientOptions>(opts =>
        {
            opts.ClientId = section["ClientId"];
            opts.ClientSecret = section["ClientSecret"];
            opts.TenantId = section["TenantId"];
            opts.Resource = section["Resource"];
            opts.OrganizationUrl = section["OrganizationUrl"];
            opts.TokenEndpoint = section["TokenEndpoint"];
            opts.GrantType = section["GrantType"] ?? "client_credentials";
            
            // Auto-detect ADFS if TenantId is "adfs" or TokenEndpoint is set
            if (string.Equals(opts.TenantId, "adfs", StringComparison.OrdinalIgnoreCase) 
                || !string.IsNullOrWhiteSpace(opts.TokenEndpoint))
            {
                opts.AuthType = D365AuthType.ADFS;
            }
        });
        
        // Get values for HttpClient setup
        var baseUrl = !string.IsNullOrWhiteSpace(section["OrganizationUrl"])
            ? section["OrganizationUrl"]!.TrimEnd('/') + "/data/"
            : !string.IsNullOrWhiteSpace(section["Resource"])
                ? section["Resource"]!.TrimEnd('/') + "/data/"
                : string.Empty;
        
        // Register HTTP client factory with named client
        services.AddHttpClient("D365Endpoint", (serviceProvider, client) =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Support self-signed certificates for on-premise D365
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
        
        // Register unified token provider (handles both Azure AD and ADFS)
        services.AddSingleton<ID365AccessTokenProvider, D365AccessTokenProvider>();
        
        // Register D365 service as scoped (one instance per request)
        services.AddScoped<ID365Service, D365Service>();
        
        return services;
    }
}
