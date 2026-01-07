namespace FlintsLabs.D365.ODataClient.Enums;

/// <summary>
/// D365 NoYes enum values for OData queries
/// </summary>
public static class D365NoYes
{
    public const string Entity = "Microsoft.Dynamics.DataEntities.NoYes";
    public const string Yes = Entity + "'Yes'";
    public const string No = Entity + "'No'";
}

/// <summary>
/// OData comparison operators
/// </summary>
public static class D365Operand
{
    public const string Equal = "eq";
    public const string NotEqual = "ne";
    public const string GreaterThan = "gt";
    public const string GreaterThanOrEqual = "ge";
    public const string LessThan = "lt";
    public const string LessThanOrEqual = "le";
    public const string And = "and";
    public const string Or = "or";
    public const string Not = "not";
    public const string Has = "has";
    public const string Asc = "asc";
    public const string Desc = "desc";
}

/// <summary>
/// Common D365 DataEntities enum values
/// </summary>
public static class D365DataEntity
{
    public static class EcoResProductType
    {
        public const string Entity = "Microsoft.Dynamics.DataEntities.EcoResProductType";
        public const string Item = Entity + "'Item'";
        public const string Service = Entity + "'Service'";
    }

    public static class VersioningDocumentState
    {
        public const string Entity = "Microsoft.Dynamics.DataEntities.VersioningDocumentState";
        public const string Confirmed = Entity + "'Confirmed'";
    }

    public static class PurchStatus
    {
        public const string Entity = "Microsoft.Dynamics.DataEntities.PurchStatus";
        public const string Backorder = Entity + "'Backorder'";
        public const string Received = Entity + "'Received'";
        public const string Invoiced = Entity + "'Invoiced'";
        public const string Canceled = Entity + "'Canceled'";
    }
}
