using FlintsLabs.D365.ODataClient.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Provides access token for D365 F&O using Microsoft Entra ID (Azure AD)
/// </summary>
public class D365AccessTokenProvider : ID365AccessTokenProvider
{
    private readonly ILogger<D365AccessTokenProvider> _logger;
    private readonly D365ClientOptions _options;
    private string? _accessToken;
    private DateTime? _expiresOn;

    public D365AccessTokenProvider(
        ILogger<D365AccessTokenProvider> logger,
        IOptions<D365ClientOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }
    
    /// <summary>
    /// Get access token, with automatic caching and refresh
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresOn.HasValue && DateTime.UtcNow < _expiresOn)
        {
            _logger.LogDebug("Access token is still valid, using cached token");
            return _accessToken;
        }

        try
        {
            _logger.LogInformation("Acquiring new access token for D365");
            
            var authBuilder = ConfidentialClientApplicationBuilder
                .Create(_options.ClientId)
                .WithTenantId(_options.TenantId)
                .WithClientSecret(_options.ClientSecret)
                .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
                .Build();

            string[] scopes = [_options.Resource + "/.default"];

            AuthenticationResult token = await authBuilder.AcquireTokenForClient(scopes).ExecuteAsync();
            _expiresOn = token.ExpiresOn.UtcDateTime;
            _accessToken = token.AccessToken;
            
            _logger.LogInformation("Successfully acquired new access token");
            return _accessToken;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to acquire access token for D365");
            throw;
        }
    }
}
