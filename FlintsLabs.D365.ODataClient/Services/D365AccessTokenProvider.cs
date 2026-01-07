using System.Text.Json;
using FlintsLabs.D365.ODataClient.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Unified access token provider for D365 F&O
/// Supports both Azure AD (Cloud) and ADFS (On-Premise) authentication
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
    /// Get access token with automatic caching and refresh
    /// Routes to Azure AD or ADFS based on configuration
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
        // Check if token is still valid (with 5 min buffer)
        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresOn.HasValue && DateTime.UtcNow.AddMinutes(5) < _expiresOn)
        {
            _logger.LogDebug("Access token is still valid, using cached token");
            return _accessToken;
        }

        // Route to appropriate auth method
        return _options.AuthType switch
        {
            D365AuthType.ADFS => await GetAdfsTokenAsync(),
            _ => await GetAzureAdTokenAsync()
        };
    }

    /// <summary>
    /// Acquire token using Azure AD / Microsoft Entra ID (MSAL)
    /// </summary>
    private async Task<string> GetAzureAdTokenAsync()
    {
        try
        {
            _logger.LogInformation("Acquiring access token from Azure AD");
            
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
            
            _logger.LogInformation("Successfully acquired Azure AD access token");
            return _accessToken;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to acquire access token from Azure AD");
            throw;
        }
    }

    /// <summary>
    /// Acquire token using ADFS (Active Directory Federation Services)
    /// </summary>
    private async Task<string> GetAdfsTokenAsync()
    {
        try
        {
            _logger.LogInformation("Acquiring access token from ADFS");
            
            if (string.IsNullOrWhiteSpace(_options.TokenEndpoint))
            {
                throw new InvalidOperationException("TokenEndpoint is required for ADFS authentication. Set D365:TokenEndpoint in configuration.");
            }

            var tokenPostData = new Dictionary<string, string>
            {
                { "tenant_id", _options.TenantId ?? "adfs" },
                { "client_id", _options.ClientId ?? string.Empty },
                { "client_secret", _options.ClientSecret ?? string.Empty },
                { "resource", _options.Resource ?? string.Empty },
                { "grant_type", _options.GrantType }
            };

            // Create handler that accepts self-signed certificates (common in on-premise)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
            };

            using var httpClient = new HttpClient(handler);
            var response = await httpClient.PostAsync(
                _options.TokenEndpoint, 
                new FormUrlEncodedContent(tokenPostData));

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to acquire ADFS token: {StatusCode} - {Error}", 
                    response.StatusCode, errorContent);
                throw new HttpRequestException($"ADFS token request failed: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);
            
            if (jsonResponse == null || !jsonResponse.TryGetValue("access_token", out var accessTokenObj))
            {
                throw new InvalidOperationException("ADFS response did not contain access_token");
            }

            _accessToken = accessTokenObj.ToString()!;
            
            // Parse expires_in if available
            if (jsonResponse.TryGetValue("expires_in", out var expiresInObj))
            {
                if (int.TryParse(expiresInObj.ToString(), out var expiresIn))
                {
                    _expiresOn = DateTime.UtcNow.AddSeconds(expiresIn);
                }
            }
            else
            {
                // Default to 1 hour if not specified
                _expiresOn = DateTime.UtcNow.AddHours(1);
            }

            _logger.LogInformation("Successfully acquired ADFS access token");
            return _accessToken;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to acquire access token from ADFS");
            throw;
        }
    }
}
