using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Transport;

internal interface ID365Transport
{
    Task<D365Response> SendRawAsync(
        D365Request request,
        CancellationToken cancellationToken);

    Task<D365Response> SendEnsuredAsync(
        D365Request request,
        CancellationToken cancellationToken);
}
