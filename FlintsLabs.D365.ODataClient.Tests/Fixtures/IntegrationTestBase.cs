using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlintsLabs.D365.ODataClient.Tests.Fixtures;

public class IntegrationTestBase
{
    protected readonly IServiceProvider ServiceProvider;
    private readonly HashSet<D365ServiceScope> _configuredScopes = [];

    public IntegrationTestBase()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("D365_RUN_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SkipException(
                "Live D365 tests require D365_RUN_INTEGRATION_TESTS=true.");
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        
        // Register D365 Clients from AppSettings
        // Note: We register both Cloud and OnPrem if available in config
        
        // Cloud Registration
        if (configuration.GetSection("D365Configs_OnCloud").Exists())
        {
            services.AddD365ODataClient(D365ServiceScope.Cloud, configuration, "D365Configs_OnCloud");
            _configuredScopes.Add(D365ServiceScope.Cloud);
        }
        
        // OnPrem Registration
        if (configuration.GetSection("D365Configs_OnPrem").Exists())
        {
            services.AddD365ODataClient(D365ServiceScope.OnPrem, configuration, "D365Configs_OnPrem");
            _configuredScopes.Add(D365ServiceScope.OnPrem);
        }

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        ServiceProvider = services.BuildServiceProvider();
    }

    protected ID365Client? GetClient(D365ServiceScope scope)
    {
        if (!_configuredScopes.Contains(scope))
            return null;

        var factory = ServiceProvider.GetRequiredService<ID365ClientFactory>();
        return factory.GetClient(scope.ToString());
    }
}
