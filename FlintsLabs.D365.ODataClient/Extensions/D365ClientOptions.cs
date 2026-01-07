namespace FlintsLabs.D365.ODataClient.Extensions;

/// <summary>
/// Authentication type for D365
/// </summary>
public enum D365AuthType
{
    /// <summary>
    /// Azure AD / Microsoft Entra ID (Cloud D365)
    /// </summary>
    AzureAD,
    
    /// <summary>
    /// ADFS - Active Directory Federation Services (On-Premise D365)
    /// </summary>
    ADFS
}

/// <summary>
/// Configuration options for D365 OData client
/// </summary>
public class D365ClientOptions
{
    /// <summary>
    /// Authentication type: AzureAD or ADFS
    /// Default: AzureAD
    /// </summary>
    public D365AuthType AuthType { get; set; } = D365AuthType.AzureAD;
    
    /// <summary>
    /// Client ID (App Registration in Azure AD or ADFS)
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Client Secret
    /// </summary>
    public string? ClientSecret { get; set; }
    
    /// <summary>
    /// Tenant ID (Azure AD) or "adfs" for ADFS
    /// </summary>
    public string? TenantId { get; set; }
    
    /// <summary>
    /// D365 F&O Resource URL (e.g., https://your-org.operations.dynamics.com)
    /// </summary>
    public string? Resource { get; set; }
    
    /// <summary>
    /// D365 Organization URL (for On-Premise, e.g., https://ax.company.com/namespaces/AXSF/)
    /// If not set, uses Resource + "/data/"
    /// </summary>
    public string? OrganizationUrl { get; set; }
    
    /// <summary>
    /// ADFS Token Endpoint (e.g., https://fs.company.com/adfs/oauth2/token)
    /// Required when AuthType = ADFS
    /// </summary>
    public string? TokenEndpoint { get; set; }
    
    /// <summary>
    /// Grant Type for ADFS (default: client_credentials)
    /// </summary>
    public string GrantType { get; set; } = "client_credentials";
    
    /// <summary>
    /// Get the base URL for OData API calls
    /// </summary>
    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(OrganizationUrl))
        {
            return OrganizationUrl.TrimEnd('/') + "/data/";
        }
        
        if (!string.IsNullOrWhiteSpace(Resource))
        {
            return Resource.TrimEnd('/') + "/data/";
        }
        
        return string.Empty;
    }
}
