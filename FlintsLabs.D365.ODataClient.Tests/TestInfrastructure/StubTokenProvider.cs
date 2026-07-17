using System.Collections.Concurrent;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;

namespace FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;

internal sealed class StubTokenProvider : ID365AccessTokenProvider
{
    private readonly string _refreshedToken;
    private readonly ConcurrentQueue<string> _rejectedTokens = new();
    private int _getCount;
    private int _refreshCount;
    private D365AccessToken _currentToken;

    public StubTokenProvider(string token, string? refreshedToken = null)
    {
        _currentToken = CreateToken(token);
        _refreshedToken = refreshedToken ?? token;
    }

    public int GetCount => Volatile.Read(ref _getCount);
    public int RefreshCount => Volatile.Read(ref _refreshCount);
    public IReadOnlyList<string> RejectedTokens => _rejectedTokens.ToArray();

    public Func<string, CancellationToken, ValueTask<D365AccessToken>>? RefreshOverride { get; init; }

    public ValueTask<D365AccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _getCount);
        return ValueTask.FromResult(Volatile.Read(ref _currentToken));
    }

    public async ValueTask<D365AccessToken> RefreshAccessTokenAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _refreshCount);
        _rejectedTokens.Enqueue(rejectedAccessToken);

        var token = RefreshOverride is null
            ? CreateToken(_refreshedToken)
            : await RefreshOverride(rejectedAccessToken, cancellationToken);
        Volatile.Write(ref _currentToken, token);
        return token;
    }

    private static D365AccessToken CreateToken(string value)
    {
        return new D365AccessToken(value, DateTimeOffset.UtcNow.AddHours(1));
    }
}
