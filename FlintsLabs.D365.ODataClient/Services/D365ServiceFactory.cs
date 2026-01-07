using FlintsLabs.D365.ODataClient.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Factory for creating named D365 service instances
/// Supports multiple D365 sources (e.g., Cloud and OnPrem)
/// </summary>
public interface ID365ServiceFactory
{
    /// <summary>
    /// Get D365 service by registered name
    /// </summary>
    /// <param name="name">The name used when registering the service</param>
    ID365Service GetService(string name);
    
    /// <summary>
    /// Get default D365 service (first registered or unnamed)
    /// </summary>
    ID365Service GetService();
}

/// <summary>
/// Implementation of D365 service factory
/// </summary>
public class D365ServiceFactory : ID365ServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, D365ClientOptions> _registrations;

    public D365ServiceFactory(
        IServiceProvider serviceProvider,
        Dictionary<string, D365ClientOptions> registrations)
    {
        _serviceProvider = serviceProvider;
        _registrations = registrations;
    }

    public ID365Service GetService(string name)
    {
        if (!_registrations.TryGetValue(name, out var options))
        {
            throw new InvalidOperationException($"D365 service '{name}' is not registered. Use AddD365ODataClient(\"{name}\", ...) in Program.cs");
        }
        
        // Create scoped service with specific options
        var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
        var loggerFactory = _serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
        
        var tokenProvider = new D365AccessTokenProvider(
            loggerFactory.CreateLogger<D365AccessTokenProvider>(),
            Microsoft.Extensions.Options.Options.Create(options));
        
        var service = new D365Service(
            httpClientFactory,
            loggerFactory.CreateLogger<D365Service>(),
            tokenProvider,
            Microsoft.Extensions.Options.Options.Create(options));
        
        return service;
    }

    public ID365Service GetService()
    {
        // Return first registered or "Default" if exists
        var defaultName = _registrations.ContainsKey("Default") ? "Default" : _registrations.Keys.FirstOrDefault();
        
        if (string.IsNullOrEmpty(defaultName))
        {
            throw new InvalidOperationException("No D365 services registered. Use AddD365ODataClient(...) in Program.cs");
        }
        
        return GetService(defaultName);
    }
}
