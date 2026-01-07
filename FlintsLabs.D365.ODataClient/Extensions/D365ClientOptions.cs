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
/// Fluent builder for D365 client configuration
/// </summary>
public class D365ClientBuilder
{
    internal D365ClientOptions Options { get; } = new();
    
    /// <summary>
    /// Use Azure AD / Microsoft Entra ID authentication (Cloud D365)
    /// </summary>
    public D365ClientBuilder UseAzureAD()
    {
        Options.AuthType = D365AuthType.AzureAD;
        return this;
    }
    
    /// <summary>
    /// Use ADFS authentication (On-Premise D365)
    /// </summary>
    public D365ClientBuilder UseADFS()
    {
        Options.AuthType = D365AuthType.ADFS;
        return this;
    }
    
    /// <summary>
    /// Set Client ID (App Registration)
    /// </summary>
    public D365ClientBuilder WithClientId(string clientId)
    {
        Options.ClientId = clientId;
        return this;
    }
    
    /// <summary>
    /// Set Client Secret
    /// </summary>
    public D365ClientBuilder WithClientSecret(string clientSecret)
    {
        Options.ClientSecret = clientSecret;
        return this;
    }
    
    /// <summary>
    /// Set Tenant ID (Azure AD) or "adfs" for ADFS
    /// </summary>
    public D365ClientBuilder WithTenantId(string tenantId)
    {
        Options.TenantId = tenantId;
        return this;
    }
    
    /// <summary>
    /// Set D365 Resource URL (e.g., https://your-org.operations.dynamics.com)
    /// </summary>
    public D365ClientBuilder WithResource(string resource)
    {
        Options.Resource = resource;
        return this;
    }
    
    /// <summary>
    /// Set Organization URL (for On-Premise, e.g., https://ax.company.com/namespaces/AXSF/)
    /// If not set, uses Resource + "/data/"
    /// </summary>
    public D365ClientBuilder WithOrganizationUrl(string organizationUrl)
    {
        Options.OrganizationUrl = organizationUrl;
        return this;
    }
    
    /// <summary>
    /// Set ADFS Token Endpoint (required for ADFS auth)
    /// </summary>
    public D365ClientBuilder WithTokenEndpoint(string tokenEndpoint)
    {
        Options.TokenEndpoint = tokenEndpoint;
        return this;
    }
    
    /// <summary>
    /// Set Grant Type (default: client_credentials)
    /// </summary>
    public D365ClientBuilder WithGrantType(string grantType)
    {
        Options.GrantType = grantType;
        return this;
    }
    
    /// <summary>
    /// Configure from IConfiguration section
    /// </summary>
    public D365ClientBuilder FromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration, string sectionName = "D365")
    {
        var section = configuration.GetSection(sectionName);
        
        Options.ClientId = section["ClientId"];
        Options.ClientSecret = section["ClientSecret"];
        Options.TenantId = section["TenantId"];
        Options.Resource = section["Resource"];
        Options.OrganizationUrl = section["OrganizationUrl"];
        Options.TokenEndpoint = section["TokenEndpoint"];
        Options.GrantType = section["GrantType"] ?? "client_credentials";
        
        // Auto-detect ADFS
        if (string.Equals(Options.TenantId, "adfs", StringComparison.OrdinalIgnoreCase) 
            || !string.IsNullOrWhiteSpace(Options.TokenEndpoint))
        {
            Options.AuthType = D365AuthType.ADFS;
        }
        
        return this;
    }
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
