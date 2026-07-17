using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Services;

public interface ID365Client
{
    D365Query<T> Entity<T>(string entity);

    D365Query<T> Entity<T>(Enum entity);

    Task<D365Response> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? payload = null,
        CancellationToken cancellationToken = default);
}
