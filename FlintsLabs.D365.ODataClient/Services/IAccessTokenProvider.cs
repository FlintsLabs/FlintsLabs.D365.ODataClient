namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Base interface for access token providers
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// Get access token for authentication
    /// </summary>
    Task<string> GetAccessTokenAsync();
}
