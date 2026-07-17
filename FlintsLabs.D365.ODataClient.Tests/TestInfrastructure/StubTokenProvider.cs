using FlintsLabs.D365.ODataClient.Services;

namespace FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;

internal sealed class StubTokenProvider(string token) : ID365AccessTokenProvider
{
    private int _getCount;

    public int GetCount => Volatile.Read(ref _getCount);

    public Task<string> GetAccessTokenAsync()
    {
        Interlocked.Increment(ref _getCount);
        return Task.FromResult(token);
    }
}
