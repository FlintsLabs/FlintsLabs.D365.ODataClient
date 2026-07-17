using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Services;

public interface ID365AccessTokenProvider
{
    ValueTask<D365AccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default);

    ValueTask<D365AccessToken> RefreshAccessTokenAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken = default);
}
