# Getting Started

## Install

```bash
dotnet add package FlintsLabs.D365.ODataClient --version 2.0.0
```

The package targets .NET 8 and .NET 10 and integrates with `Microsoft.Extensions.DependencyInjection`.

## Register Dataverse

```csharp
using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;

builder.Services.AddD365ODataClient("Sales", d365 => d365
    .UseAzureAD()
    .WithOrganizationUrl(
        "https://contoso.api.crm5.dynamics.com/api/data/v9.2")
    .WithResource("https://contoso.api.crm5.dynamics.com")
    .WithTenantId(builder.Configuration["D365:Sales:TenantId"]!)
    .WithClientId(builder.Configuration["D365:Sales:ClientId"]!)
    .WithClientSecret(builder.Configuration["D365:Sales:ClientSecret"]!)
    .WithScope("https://contoso.api.crm5.dynamics.com/.default")
    .WithBooleanFormatting(D365BooleanFormatting.Literal));
```

`OrganizationUrl` may include the Dataverse API version. The client normalizes it with a trailing slash. `Resource` is the service origin used for authentication.

Do not put a real secret in source control. Resolve it from an environment variable, secret manager, or platform-managed configuration provider.

## Register F&O

```csharp
builder.Services.AddD365ODataClient("Finance", d365 => d365
    .UseAzureAD()
    .WithResource("https://contoso.operations.dynamics.com")
    .WithTenantId(builder.Configuration["D365:Finance:TenantId"]!)
    .WithClientId(builder.Configuration["D365:Finance:ClientId"]!)
    .WithClientSecret(builder.Configuration["D365:Finance:ClientSecret"]!));
```

Without `OrganizationUrl`, the OData base is `Resource + "/data/"`. Without an explicit scope, Azure AD uses `Resource + "/.default"`.

## Register from Configuration

```json
{
  "D365": {
    "Sales": {
      "TenantId": "00000000-0000-0000-0000-000000000000",
      "ClientId": "00000000-0000-0000-0000-000000000000",
      "ClientSecret": "load-from-a-secret-provider",
      "Resource": "https://contoso.api.crm5.dynamics.com",
      "OrganizationUrl": "https://contoso.api.crm5.dynamics.com/api/data/v9.2",
      "Scope": "https://contoso.api.crm5.dynamics.com/.default",
      "BooleanFormatting": "Literal",
      "Retry": {
        "MaxReadRetries": 2,
        "BaseDelay": "00:00:00.250",
        "MaxDelay": "00:00:10",
        "UseJitter": true
      }
    }
  }
}
```

```csharp
builder.Services.AddD365ODataClient(
    "Sales",
    builder.Configuration,
    "D365:Sales");
```

Configuration auto-detects ADFS when `TenantId` is `adfs`, or when a token endpoint is present and the tenant value is not a GUID. Prefer explicit fluent `UseADFS()` when constructing configuration in code.

## Define an Entity

```csharp
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;

public sealed class SalesOrder
{
    [OdataKey]
    [JsonPropertyName("SalesOrderNumber")]
    public string Number { get; set; } = string.Empty;

    [OdataKey]
    [JsonPropertyName("dataAreaId")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("OrderStatus")]
    public string? Status { get; set; }
}
```

`[JsonPropertyName]` controls OData property names. Multiple `[OdataKey]` properties form a composite key.

For Dataverse primary keys declared as Edm.Guid, use `Guid`, not `string`:

```csharp
[OdataKey]
[JsonPropertyName("rvl_egrheadid")]
public Guid Id { get; set; }
```

A string value is correctly quoted as an OData string; changing its runtime appearance to a GUID does not change its model type.

## Resolve a Client

With one/default registration, inject `ID365Client`:

```csharp
public sealed class Repository(ID365Client d365)
{
}
```

For multiple registrations, inject `ID365ClientFactory`:

```csharp
var client = factory.GetClient("Sales");
```

A named root client is reused. Every call to `Entity<T>()` creates a new mutable query builder; do not share one query instance across parallel tasks.

## Read

```csharp
var row = await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order =>
        order.Number == salesOrderNumber &&
        order.Company == company)
    .FirstOrDefaultAsync(cancellationToken);
```

`row is null` means D365 successfully returned a valid empty collection. A 401, 404 collection response, 500, timeout, network failure, invalid JSON, or malformed OData envelope throws.

```csharp
var rows = await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order => order.Company == company)
    .Select(order => new { order.Number, order.Status })
    .OrderByDescending(order => order.Number)
    .PageSize(250)
    .Take(1000)
    .ToListAsync(cancellationToken);
```

## Create

An untyped create accepts an empty success body:

```csharp
var response = await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .AddAsync(
        new
        {
            SalesOrderNumber = salesOrderNumber,
            dataAreaId = company
        },
        cancellationToken);
```

A typed create requires D365 to return a JSON representation:

```csharp
var response = await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .AddHeader("Prefer", "return=representation")
    .AddAsync<SalesOrder>(payload, cancellationToken);
```

## Update and Delete

```csharp
await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order =>
        order.Number == salesOrderNumber &&
        order.Company == company)
    .UpdateAsync(
        new { OrderStatus = "Backorder" },
        cancellationToken);

await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order =>
        order.Number == salesOrderNumber &&
        order.Company == company)
    .DeleteAsync(cancellationToken);
```

Write expressions may contain key equality only and must include every annotated key. For compatibility, `.AddIdentity("field", value)` is still supported.

## Handle Failures

```csharp
try
{
    var rows = await client
        .Entity<SalesOrder>("SalesOrderHeadersV2")
        .ToListAsync(cancellationToken);
}
catch (D365AuthenticationException exception)
{
    // Actual authentication failure after the one-401 refresh attempt.
}
catch (D365HttpException exception)
{
    // D365 returned a non-success HTTP status.
}
catch (D365TransportException exception)
{
    // No HTTP response was available: transport or timeout.
}
catch (D365ProtocolException exception)
{
    // Successful HTTP response violated the expected OData contract.
}
catch (D365SerializationException exception)
{
    // Response JSON could not be converted to the requested model.
}
```

See [Error handling](error-handling.md) before implementing retry or recovery logic.

## Next Steps

- [Queries and pagination](query-and-pagination.md)
- [Mutations and responses](mutations-and-responses.md)
- [Authentication and parallelism](authentication-and-parallelism.md)
- [Security and logging](security-and-logging.md)
