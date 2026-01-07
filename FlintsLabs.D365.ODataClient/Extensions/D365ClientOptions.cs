namespace FlintsLabs.D365.ODataClient.Extensions;

/// <summary>
/// Configuration options for D365 OData client
/// </summary>
public class D365ClientOptions
{
    /// <summary>
    /// Azure AD Client ID (App Registration)
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Azure AD Client Secret
    /// </summary>
    public string? ClientSecret { get; set; }
    
    /// <summary>
    /// Azure AD Tenant ID
    /// </summary>
    public string? TenantId { get; set; }
    
    /// <summary>
    /// D365 F&O Resource URL (e.g., https://your-org.operations.dynamics.com)
    /// </summary>
    public string? Resource { get; set; }
}
