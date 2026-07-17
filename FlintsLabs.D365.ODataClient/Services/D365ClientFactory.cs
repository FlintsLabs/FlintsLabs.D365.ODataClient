using System.Collections.Concurrent;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlintsLabs.D365.ODataClient.Services;

public interface ID365ClientFactory
{
    ID365Client GetClient();

    ID365Client GetClient(string name);
}

internal sealed record D365ClientRegistration(string Name);

internal sealed class D365ClientFactory : ID365ClientFactory
{
    private readonly IReadOnlyList<D365ClientRegistration> _registrations;
    private readonly IReadOnlyDictionary<string, D365ClientRegistration> _registrationsByName;
    private readonly IOptionsMonitor<D365ClientOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, Lazy<ID365Client>> _clients =
        new(StringComparer.Ordinal);

    public D365ClientFactory(
        IEnumerable<D365ClientRegistration> registrations,
        IOptionsMonitor<D365ClientOptions> options,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _registrations = registrations.ToArray();
        _registrationsByName = _registrations.ToDictionary(
            registration => registration.Name,
            StringComparer.Ordinal);
        _options = options;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public ID365Client GetClient()
    {
        var defaultName = _registrationsByName.ContainsKey("Default")
            ? "Default"
            : _registrations.FirstOrDefault()?.Name;
        if (defaultName is null)
        {
            throw new InvalidOperationException(
                "No D365 clients are registered. Use AddD365ODataClient(...) during service configuration.");
        }

        return GetClient(defaultName);
    }

    public ID365Client GetClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_registrationsByName.ContainsKey(name))
        {
            throw new InvalidOperationException(
                $"D365 client '{name}' is not registered. Use AddD365ODataClient(\"{name}\", ...) during service configuration.");
        }

        return _clients.GetOrAdd(
            name,
            static (clientName, factory) => new Lazy<ID365Client>(
                () => factory.CreateClient(clientName),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this).Value;
    }

    private ID365Client CreateClient(string name)
    {
        var clientOptions = _options.Get(name);
        var tokenProvider = new D365AccessTokenProvider(
            _loggerFactory.CreateLogger<D365AccessTokenProvider>(),
            _httpClientFactory,
            Options.Create(clientOptions));
        var transport = new D365Transport(
            _httpClientFactory,
            _loggerFactory.CreateLogger<D365Transport>(),
            tokenProvider,
            clientOptions);

        return new D365Client(
            _httpClientFactory,
            _loggerFactory.CreateLogger<D365Client>(),
            tokenProvider,
            clientOptions,
            transport);
    }
}
