# FlintsLabs.D365.ODataClient

A fluent, strongly typed OData client for Microsoft Dynamics 365 Finance and Operations and Microsoft Dataverse.

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512bd4)
![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512bd4)
[![NuGet](https://img.shields.io/nuget/v/FlintsLabs.D365.ODataClient.svg)](https://www.nuget.org/packages/FlintsLabs.D365.ODataClient)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Version 2 is a breaking, fail-closed release. High-level reads and mutations no longer convert HTTP, authentication, transport, timeout, protocol, or serialization failures into `null`, empty collections, zero, or empty strings. Applications can now distinguish a successful empty query from an unavailable or rejected request.

## Highlights

- Fluent `D365Query<T>` API with LINQ-to-OData translation.
- Azure AD / Microsoft Entra ID client credentials, Azure Managed Identity, and ADFS client credentials.
- Named clients for multiple F&O and Dataverse endpoints.
- Typed exceptions with HTTP status, D365 error details, request ID, retry guidance, and mutation outcome.
- Explicit raw HTTP responses through `ID365Client.SendAsync`.
- Bounded, opt-in retries for reads only and one token refresh after an actual HTTP 401.
- Validated OData pagination with no partial-list returns.
- `[OdataKey]` support for key-based update and delete expressions.
- Safe OData literal formatting and URI encoding, including LINQ coalesce (`??`).
- Shared token cache and single-flight refresh for parallel calls through one service provider.

## Compatibility

| Package | Target frameworks | D365 endpoints |
| --- | --- | --- |
| 2.2.0 | .NET 8 and .NET 10 | F&O OData and Dataverse Web API with fluent or configuration-driven Azure AD/Managed Identity; existing ADFS client-credential deployments |
| 2.1.0 | .NET 8 and .NET 10 | F&O OData and Dataverse Web API with Azure AD client credentials or Managed Identity; existing ADFS client-credential deployments |
| 2.0.0 | .NET 8 and .NET 10 | F&O OData, Dataverse Web API, and existing ADFS client-credential deployments |

## Installation

```bash
dotnet add package FlintsLabs.D365.ODataClient --version 2.2.0
```

## Security Notice

Version 2.2.0 continues to preserve the package's existing TLS behavior for compatibility: the registered D365 and authentication HTTP handlers accept any server certificate. This disables certificate-chain and hostname validation and is unsafe on untrusted networks. Use the package only on a trusted network path while this compatibility behavior remains, and do not treat TLS peer identity as verified.

See [Security and logging](docs/v2/security-and-logging.md) before production deployment. Never commit client secrets or log `D365Response.RawBody` / `D365Exception.ResponseBody` without application-level redaction.

## Quick Start

Define a model. Use `Guid` for an Edm.Guid key; a C# `string` key is emitted as an OData string literal and is therefore quoted.

```csharp
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;

public sealed class EgrHead
{
    [OdataKey]
    [JsonPropertyName("rvl_egrheadid")]
    public Guid Id { get; set; }

    [JsonPropertyName("rvl_wmsstatus")]
    public bool? WmsStatus { get; set; }
}
```

Register a Dataverse client:

```csharp
using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;

builder.Services.AddD365ODataClient(d365 => d365
    .UseAzureAD()
    .WithOrganizationUrl(
        "https://contoso.api.crm5.dynamics.com/api/data/v9.2")
    .WithResource("https://contoso.api.crm5.dynamics.com")
    .WithTenantId(builder.Configuration["D365:TenantId"]!)
    .WithClientId(builder.Configuration["D365:ClientId"]!)
    .WithClientSecret(builder.Configuration["D365:ClientSecret"]!)
    .WithScope("https://contoso.api.crm5.dynamics.com/.default")
    .WithBooleanFormatting(D365BooleanFormatting.Literal));
```

Inject the root client and issue a read:

```csharp
using FlintsLabs.D365.ODataClient.Services;

public sealed class EgrRepository(ID365Client dataverse)
{
    public Task<EgrHead?> FindAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return dataverse
            .Entity<EgrHead>("rvl_egrheads")
            .Where(head => head.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
```

`null` now means the server returned a successful, valid collection response with no matching row. HTTP 404, 401, 500, network errors, invalid JSON, and malformed OData envelopes throw instead.

## Configuration

### Azure AD for F&O

When `OrganizationUrl` is omitted, `Resource + "/data/"` is used as the OData base URL.

```csharp
builder.Services.AddD365ODataClient(d365 => d365
    .UseAzureAD()
    .WithResource("https://contoso.operations.dynamics.com")
    .WithTenantId(configuration["D365:TenantId"]!)
    .WithClientId(configuration["D365:ClientId"]!)
    .WithClientSecret(configuration["D365:ClientSecret"]!));
```

If `WithScope(...)` is not supplied, Azure AD uses `Resource + "/.default"`.

### Azure Managed Identity for F&O

Use the System-assigned identity attached to the Azure workload:

```csharp
builder.Services.AddD365ODataClient(d365 => d365
    .UseSystemAssignedManagedIdentity()
    .WithResource("https://contoso.operations.dynamics.com"));
```

Or select an attached User-assigned identity by its application (client) ID:

```csharp
builder.Services.AddD365ODataClient(d365 => d365
    .UseUserAssignedManagedIdentity(managedIdentityClientId)
    .WithResource("https://contoso.operations.dynamics.com"));
```

Managed Identity can also be selected through `FromConfiguration()` in version 2.2.0. There is no fallback from Managed Identity to a client secret. If `WithScope(...)` is omitted, the token resource is `Resource + "/.default"`.

The identity must be attached to the hosting Azure workload, and its application (client) ID must be registered and mapped to the intended D365 F&O user. For a User-assigned identity, pass the client ID, not its object/principal ID or Azure resource ID.

### ADFS

The existing ADFS form-post flow is retained. Confirm the exact token endpoint and client-credential support with the owner of the on-premises deployment.

```csharp
builder.Services.AddD365ODataClient(d365 => d365
    .UseADFS()
    .WithTokenEndpoint("https://fs.contoso.local/adfs/oauth2/token")
    .WithClientId(configuration["D365:ClientId"]!)
    .WithClientSecret(configuration["D365:ClientSecret"]!)
    .WithResource("https://ax.contoso.local")
    .WithOrganizationUrl(
        "https://ax.contoso.local/namespaces/AXSF/")
    .WithGrantType("client_credentials"));
```

### appsettings.json

```json
{
  "D365": {
    "AuthType": "AzureAD",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "ClientSecret": "load-this-from-a-secret-provider",
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
```

```csharp
builder.Services.AddD365ODataClient(
    builder.Configuration,
    "D365");
```

User-assigned Managed Identity configuration does not require a client secret:

```json
{
  "D365Dataverse": {
    "AuthType": "ManagedIdentity",
    "ManagedIdentityClientId": "00000000-0000-0000-0000-000000000000",
    "Resource": "https://contoso.api.crm5.dynamics.com",
    "OrganizationUrl": "https://contoso.api.crm5.dynamics.com/api/data/v9.2",
    "Scope": "https://contoso.api.crm5.dynamics.com/.default",
    "BooleanFormatting": "Literal"
  }
}
```

```csharp
builder.Services.AddD365ODataClient(
    "D365Dataverse",
    builder.Configuration,
    "D365Dataverse");
```

Omit `ManagedIdentityClientId` to use System-assigned Managed Identity. `AuthType` accepts only the names `AzureAD`, `ADFS`, and `ManagedIdentity` (case-insensitive); invalid, empty, or numeric values fail during registration. If `AuthType` is absent, the legacy Azure AD/ADFS detection remains in effect.

### Named clients

```csharp
builder.Services.AddD365ODataClient(
    "Finance",
    configuration,
    "D365:Finance");
builder.Services.AddD365ODataClient(
    "Sales",
    configuration,
    "D365:Sales");

var factory = serviceProvider.GetRequiredService<ID365ClientFactory>();
var finance = factory.GetClient("Finance");
var sales = factory.GetClient("Sales");
```

`ID365ClientFactory` and each named root client are singleton-scoped within their service provider. Create a fresh `.Entity<T>(...)` query for each parallel operation; a mutable `D365Query<T>` instance is not thread-safe.

## Reads

```csharp
var rows = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.WmsStatus == false)
    .Select(head => new { head.Id, head.WmsStatus })
    .OrderBy(head => head.Id)
    .PageSize(250)
    .ToListAsync(cancellationToken);

var count = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.WmsStatus == false)
    .LongCountAsync(cancellationToken);
```

`LongCountAsync` requires a valid non-negative 64-bit `@odata.count`. `CountAsync` performs a checked conversion to `int` and throws on overflow. Pagination validates every next link and throws without returning a partial list if any page fails.

## Update and Delete

With `[OdataKey]`, a key-only equality expression becomes the OData entity key:

```csharp
var updated = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == headId)
    .UpdateAsync(
        new { rvl_wmsstatus = false },
        cancellationToken);

var deleted = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == headId)
    .DeleteAsync(cancellationToken);
```

Composite keys are supported by annotating every key property and supplying every key as equality joined with `&&`. The compatibility fallback remains available:

```csharp
await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddIdentity("rvl_egrheadid", headId)
    .UpdateAsync(
        new { rvl_wmsstatus = false },
        cancellationToken);
```

High-level mutations throw for every non-2xx status, including DELETE 404. Successful no-content responses are valid for untyped mutations.

## Create and Responses

```csharp
var response = await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddHeader("Prefer", "return=representation")
    .AddAsync<EgrHead>(
        new { rvl_wmsstatus = false },
        cancellationToken);

Console.WriteLine(response.StatusCode);
Console.WriteLine(response.RequestId);
Console.WriteLine(response.Value?.Id);
```

A typed POST requires a non-empty JSON response body. If D365 accepted the mutation but the response body is missing or cannot be deserialized, the exception carries `MutationOutcome = SucceededOrAccepted`; do not blindly POST again.

## Failure Handling

```csharp
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Models;

try
{
    await client
        .Entity<EgrHead>("rvl_egrheads")
        .Where(head => head.Id == headId)
        .UpdateAsync(new { rvl_wmsstatus = false }, cancellationToken);
}
catch (D365HttpException exception)
{
    logger.LogWarning(
        "D365 rejected request: HTTP {Status}; code={Code}; request={RequestId}",
        exception.StatusCode,
        exception.D365ErrorCode,
        exception.RequestId);
}
catch (D365TransportException exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    // Query by the exact business/correlation key before deciding to retry.
}
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    // Cancellation after send does not prove the mutation was rolled back.
}
```

Do not interpret `IsTransient` as permission to retry a mutation. Use `MutationOutcome` and application-specific reconciliation.

## Raw HTTP

`SendAsync` is the explicit raw-status API. It returns every HTTP response that reaches the client, including 4xx and 5xx; transport, timeout, and cancellation still throw because no HTTP response exists.

```csharp
var response = await client.SendAsync(
    HttpMethod.Get,
    "rvl_egrheads?$top=1",
    cancellationToken: cancellationToken);

if (!response.IsSuccessStatusCode)
{
    response.EnsureSuccessStatusCode();
}
```

Use the fluent query methods for fail-closed deserialization and the raw API only when the caller intentionally owns status handling.

## Retry and Cancellation

Automatic retry is disabled by default. Opt in only for safe reads:

```csharp
builder.Services.AddD365ODataClient(d365 => d365
    // Authentication and endpoint configuration omitted.
    .ConfigureRetry(retry =>
    {
        retry.MaxReadRetries = 2;
        retry.BaseDelay = TimeSpan.FromMilliseconds(250);
        retry.MaxDelay = TimeSpan.FromSeconds(10);
        retry.UseJitter = true;
    }));
```

Only GET and HEAD may retry transient transport/timeouts or HTTP 408, 429, 500, 502, 503, and 504. POST, PATCH, and DELETE are not automatically retried for ambiguous failures. All async terminal methods accept `CancellationToken`.

## Documentation

- [Documentation index](docs/v2/README.md)
- [Getting started](docs/v2/getting-started.md)
- [Migration from version 1](docs/v2/migration-from-v1.md)
- [Error handling](docs/v2/error-handling.md)
- [Mutations and responses](docs/v2/mutations-and-responses.md)
- [Retry, timeout, and cancellation](docs/v2/retry-timeout-cancellation.md)
- [Authentication and parallelism](docs/v2/authentication-and-parallelism.md)
- [Queries and pagination](docs/v2/query-and-pagination.md)
- [Security and logging](docs/v2/security-and-logging.md)
- [BioWMS recovery pattern](docs/v2/biowms-recovery-pattern.md)
- [Compiled examples](samples/FlintsLabs.D365.ODataClient.V2.Examples/Program.cs)
- [Changelog](CHANGELOG.md)

## Development

```bash
dotnet restore
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj \
  -c Release \
  --filter "Category!=Integration"
```

Live integration tests are opt-in through `D365_RUN_INTEGRATION_TESTS=true` and require local configuration. Normal build and package workflows do not contact D365.

## License

MIT. See [LICENSE](https://github.com/FlintsLabs/FlintsLabs.D365.ODataClient/blob/main/LICENSE) in the repository.
