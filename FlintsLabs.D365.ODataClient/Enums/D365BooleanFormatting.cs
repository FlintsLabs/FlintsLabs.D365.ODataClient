namespace FlintsLabs.D365.ODataClient.Enums;

/// <summary>
/// Strategy for formatting boolean values in OData filters
/// </summary>
public enum D365BooleanFormatting
{
    /// <summary>
    /// Default: Format as Microsoft.Dynamics.DataEntities.NoYes'Yes'/'No'
    /// Used by D365 Finance & Operations standard entities
    /// </summary>
    NoYesEnum,
    
    /// <summary>
    /// Format as standard OData literals: true/false
    /// Used by Dataverse / CRM and some standard OData services
    /// </summary>
    Literal
}
