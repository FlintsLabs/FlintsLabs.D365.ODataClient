using System;

namespace FlintsLabs.D365.ODataClient.Attributes;

/// <summary>
/// Marks a property as the OData key for update/delete operations.
/// Use one attribute per key property (supports composite keys).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class OdataKeyAttribute : Attribute
{
}
