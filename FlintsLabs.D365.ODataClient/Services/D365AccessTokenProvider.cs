using System.Text.Json;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Provides cached D365 access tokens for Azure AD, Managed Identity, or ADFS authentication.
/// </summary>
internal sealed class D365AccessTokenProvider : ID365AccessTokenProvider
{
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);

    private readonly ILogger<D365AccessTokenProvider> _logger;
    private readonly D365ClientOptions _options;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly Func<bool, CancellationToken, ValueTask<D365AccessToken>> _acquireToken;
    private readonly IManagedIdentityApplication? _managedIdentityApplication;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private D365AccessToken? _accessToken;

    public D365AccessTokenProvider(
        ILogger<D365AccessTokenProvider> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<D365ClientOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _managedIdentityApplication = CreateManagedIdentityApplication(_options);
        _acquireToken = AcquireTokenAsync;
    }

    internal D365AccessTokenProvider(
        ILogger<D365AccessTokenProvider> logger,
        D365ClientOptions options,
        Func<bool, CancellationToken, ValueTask<D365AccessToken>> acquireToken)
    {
        _logger = logger;
        _options = options;
        _httpClientFactory = null;
        _acquireToken = acquireToken;
    }

    public async ValueTask<D365AccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cachedToken = Volatile.Read(ref _accessToken);
        if (IsTokenValid(cachedToken))
            return cachedToken!;

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedToken = Volatile.Read(ref _accessToken);
            if (IsTokenValid(cachedToken))
                return cachedToken!;

            return await AcquireAndCacheTokenAsync(false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async ValueTask<D365AccessToken> RefreshAccessTokenAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedAccessToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachedToken = Volatile.Read(ref _accessToken);
            if (IsTokenValid(cachedToken)
                && !string.Equals(cachedToken!.Value, rejectedAccessToken, StringComparison.Ordinal))
            {
                return cachedToken;
            }

            if (string.Equals(cachedToken?.Value, rejectedAccessToken, StringComparison.Ordinal))
                Volatile.Write(ref _accessToken, null);

            return await AcquireAndCacheTokenAsync(true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async ValueTask<D365AccessToken> AcquireAndCacheTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        D365AccessToken token;
        try
        {
            token = await _acquireToken(forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        catch (MsalException exception)
        {
            throw new D365TokenAcquisitionException(
                "D365 access token acquisition failed for the selected authentication method.",
                _options.AuthType,
                exception is MsalServiceException { IsRetryable: true },
                exception);
        }

        if (string.IsNullOrWhiteSpace(token.Value))
            throw new InvalidOperationException("The token authority returned an empty access token.");

        Volatile.Write(ref _accessToken, token);
        return token;
    }

    private static bool IsTokenValid(D365AccessToken? token)
    {
        return token is not null
               && !string.IsNullOrWhiteSpace(token.Value)
               && DateTimeOffset.UtcNow.Add(RefreshBuffer) < token.ExpiresOn;
    }

    private ValueTask<D365AccessToken> AcquireTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        return _options.AuthType switch
        {
            D365AuthType.ADFS => GetAdfsTokenAsync(cancellationToken),
            D365AuthType.ManagedIdentity => GetManagedIdentityTokenAsync(forceRefresh, cancellationToken),
            _ => GetAzureAdTokenAsync(cancellationToken)
        };
    }

    private static IManagedIdentityApplication? CreateManagedIdentityApplication(
        D365ClientOptions options)
    {
        if (options.AuthType != D365AuthType.ManagedIdentity)
            return null;

        var managedIdentityId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned
            : ManagedIdentityId.WithUserAssignedClientId(options.ManagedIdentityClientId);

        return ManagedIdentityApplicationBuilder
            .Create(managedIdentityId)
            .Build();
    }

    private async ValueTask<D365AccessToken> GetManagedIdentityTokenAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        TryLog(() => _logger.LogDebug("Acquiring D365 access token using Managed Identity"));

        var resource = !string.IsNullOrWhiteSpace(_options.Scope)
            ? _options.Scope
            : string.IsNullOrWhiteSpace(_options.Resource)
                ? null
                : _options.Resource.TrimEnd('/') + "/.default";

        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new InvalidOperationException(
                "Scope or Resource is required for Managed Identity authentication.");
        }

        var application = _managedIdentityApplication
                          ?? throw new InvalidOperationException(
                              "Managed Identity authentication is not initialized.");
        var result = await application
            .AcquireTokenForManagedIdentity(resource)
            .WithForceRefresh(forceRefresh)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new D365AccessToken(result.AccessToken, result.ExpiresOn);
    }

    private async ValueTask<D365AccessToken> GetAzureAdTokenAsync(
        CancellationToken cancellationToken)
    {
        TryLog(() => _logger.LogDebug("Acquiring D365 access token from Azure AD"));

        var application = ConfidentialClientApplicationBuilder
            .Create(_options.ClientId)
            .WithTenantId(_options.TenantId)
            .WithClientSecret(_options.ClientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .Build();

        var scopes = !string.IsNullOrWhiteSpace(_options.Scope)
            ? new[] { _options.Scope }
            : new[] { _options.Resource?.TrimEnd('/') + "/.default" };
        var result = await application
            .AcquireTokenForClient(scopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new D365AccessToken(result.AccessToken, result.ExpiresOn);
    }

    private async ValueTask<D365AccessToken> GetAdfsTokenAsync(
        CancellationToken cancellationToken)
    {
        TryLog(() => _logger.LogDebug("Acquiring D365 access token from ADFS"));

        if (string.IsNullOrWhiteSpace(_options.TokenEndpoint))
        {
            throw new InvalidOperationException(
                "TokenEndpoint is required for ADFS authentication. Set D365:TokenEndpoint in configuration.");
        }

        var tokenPostData = new Dictionary<string, string>
        {
            ["tenant_id"] = _options.TenantId ?? "adfs",
            ["client_id"] = _options.ClientId ?? string.Empty,
            ["client_secret"] = _options.ClientSecret ?? string.Empty,
            ["resource"] = _options.Resource ?? string.Empty,
            ["grant_type"] = _options.GrantType
        };

        var httpClient = _httpClientFactory?.CreateClient(_options.AuthHttpClientName)
                         ?? throw new InvalidOperationException(
                             "IHttpClientFactory is required for ADFS token acquisition.");
        using var content = new FormUrlEncodedContent(tokenPostData);
        using var response = await httpClient
            .PostAsync(_options.TokenEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        var responseContent = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"ADFS token request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseContent);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("access_token", out var tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(tokenElement.GetString()))
        {
            throw new InvalidOperationException("ADFS response did not contain a valid access_token.");
        }

        var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
        if (document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
            && TryReadSeconds(expiresInElement, out var expiresInSeconds))
        {
            expiresOn = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        }

        return new D365AccessToken(tokenElement.GetString()!, expiresOn);
    }

    private static bool TryReadSeconds(JsonElement element, out int seconds)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out seconds) && seconds >= 0;
        if (element.ValueKind == JsonValueKind.String)
            return int.TryParse(element.GetString(), out seconds) && seconds >= 0;

        seconds = 0;
        return false;
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Diagnostics must not change token acquisition behavior.
        }
    }
}
