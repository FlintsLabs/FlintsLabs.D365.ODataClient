using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace FlintsLabs.D365.ODataClient.Extensions;

/// <summary>
/// Extension methods for registering D365 OData client services
/// </summary>
public static class ServiceCollectionExtensions
{
    // Store registrations for factory (thread-safe)
    private static readonly ConcurrentDictionary<string, D365ClientOptions> _registrations = new();
    
    /// <summary>
    /// Add named D365 OData client service with fluent configuration
    /// Supports multiple D365 sources (Cloud, OnPrem, etc.)
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="name">Unique name for this D365 instance</param>
    /// <param name="configure">Fluent configuration builder</param>
    /// <example>
    /// <code>
    /// builder.Services.AddD365ODataClient("Cloud", d365 => 
    /// {
    ///     d365.UseAzureAD()
    ///         .WithClientId("client-id")
    ///         .WithClientSecret("secret")
    ///         .WithTenantId("tenant-id")
    ///         .WithResource("https://cloud.operations.dynamics.com");
    /// });
    /// 
    /// builder.Services.AddD365ODataClient("OnPrem", d365 => 
    /// {
    ///     d365.UseADFS()
    ///         .WithTokenEndpoint("https://fs.company.com/adfs/oauth2/token")
    ///         .WithClientId("client-id")
    ///         .WithClientSecret("secret")
    ///         .WithResource("https://ax.company.com")
    ///         .WithOrganizationUrl("https://ax.company.com/namespaces/AXSF/");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        string name,
        Action<D365ClientBuilder> configure)
    {
        var builder = new D365ClientBuilder();
        configure(builder);
        
        // Set HttpClient name based on service name
        var httpClientName = $"D365Endpoint_{name}";
        builder.Options.HttpClientName = httpClientName;
        
        // Check for duplicate registration (thread-safe)
        if (!_registrations.TryAdd(name, builder.Options))
            throw new InvalidOperationException($"D365 client '{name}' is already registered. Use a unique name for each client.");
        
        // Register HttpClient for this instance
        services.AddHttpClient($"D365Endpoint_{name}", client =>
        {
            var baseUrl = builder.Options.GetBaseUrl();
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
        
        // Register factory (singleton, handles all named services)
        services.AddSingleton<ID365ServiceFactory>(sp => 
            new D365ServiceFactory(sp, new Dictionary<string, D365ClientOptions>(_registrations)));

        // Register default ID365Service (scoped) for direct injection
        services.AddScoped<ID365Service>(sp => sp.GetRequiredService<ID365ServiceFactory>().GetService());
        
        return services;
    }
    
    /// <summary>
    /// Add default D365 OData client service with fluent configuration
    /// </summary>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        Action<D365ClientBuilder> configure)
    {
        return services.AddD365ODataClient("Default", configure);
    }
    
    /// <summary>
    /// Add D365 OData client from configuration section with fluent builder
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="name">Unique name for this D365 instance</param>
    /// <param name="configuration">Configuration root</param>
    /// <param name="sectionName">Section name in config</param>
    /// <example>
    /// <code>
    /// // appsettings.json:
    /// // { "D365Cloud": { "ClientId": "...", "Resource": "..." } }
    /// // { "D365OnPrem": { "TokenEndpoint": "...", "Resource": "..." } }
    /// 
    /// builder.Services.AddD365ODataClient("Cloud", builder.Configuration, "D365Cloud");
    /// builder.Services.AddD365ODataClient("OnPrem", builder.Configuration, "D365OnPrem");
    /// </code>
    /// </example>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string sectionName)
    {
        return services.AddD365ODataClient(name, d365 => 
            d365.FromConfiguration(configuration, sectionName));
    }
    
    /// <summary>
    /// Add named D365 OData client service using D365ServiceScope enum
    /// </summary>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        D365ServiceScope scope,
        Action<D365ClientBuilder> configure)
    {
        return services.AddD365ODataClient(scope.ToString(), configure);
    }

    /// <summary>
    /// Add named D365 OData client from configuration using D365ServiceScope enum
    /// </summary>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        D365ServiceScope scope,
        IConfiguration configuration,
        string sectionName)
    {
        return services.AddD365ODataClient(scope.ToString(), configuration, sectionName);
    }
    
    /// <summary>
    /// Add default D365 OData client from configuration section
    /// </summary>
    public static IServiceCollection AddD365ODataClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "D365")
    {
        return services.AddD365ODataClient("Default", configuration, sectionName);
    }
}
