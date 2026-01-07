using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlintsLabs.D365.ODataClient.Tests.Fixtures;

public class IntegrationTestBase
{
    protected readonly IServiceProvider ServiceProvider;

    public IntegrationTestBase()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();
        
        // Register D365 Clients from AppSettings
        // Note: We register both Cloud and OnPrem if available in config
        
        // Cloud Registration
        if (configuration.GetSection("D365Configs_OnCloud").Exists())
        {
            services.AddD365ODataClient(D365ServiceScope.Cloud, configuration, "D365Configs_OnCloud");
        }
        
        // OnPrem Registration
        if (configuration.GetSection("D365Configs_OnPrem").Exists())
        {
            services.AddD365ODataClient(D365ServiceScope.OnPrem, configuration, "D365Configs_OnPrem");
        }

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        ServiceProvider = services.BuildServiceProvider();
    }

    protected ID365Service GetService(D365ServiceScope scope)
    {
        var factory = ServiceProvider.GetRequiredService<ID365ServiceFactory>();
        return factory.GetService(scope.ToString());
    }
}
