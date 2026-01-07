# FlintsLabs.D365.ODataClient

A fluent OData client for Microsoft Dynamics 365 Finance & Operations.

[![NuGet](https://img.shields.io/nuget/v/FlintsLabs.D365.ODataClient.svg)](https://www.nuget.org/packages/FlintsLabs.D365.ODataClient)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- 🔗 **Fluent API** - Chainable query builder with IntelliSense support
- 🔍 **LINQ Support** - Write queries using lambda expressions
- 🏢 **Cross-Company** - Query across legal entities
- 🔐 **Multi-Auth Support** - Azure AD (Cloud), ADFS (On-Premise), and **Dataverse**
- 📦 **CRUD Operations** - Full Create, Read, Update, Delete support
- 🌐 **Multi-Source** - Connect to multiple D365 instances (F&O, Dataverse) simultaneously

## Installation

```bash
dotnet add package FlintsLabs.D365.ODataClient
```

---

## Configuration

### Option 1: Azure AD (Cloud D365)

```csharp
// Program.cs - Fluent Builder using Enum
builder.Services.AddD365ODataClient(d365 => 
{
    d365.UseAzureAD()
        .WithClientId("your-client-id")
        .WithClientSecret("your-client-secret")
        .WithTenantId("your-tenant-id")
        .WithResource("https://your-org.operations.dynamics.com");
});
```

```json
// appsettings.json
{
  "D365": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "TenantId": "your-tenant-id",
    "Resource": "https://your-org.operations.dynamics.com"
  }
}
```

```csharp
// Or from configuration
builder.Services.AddD365ODataClient(builder.Configuration, "D365");
```

---

### Option 2: ADFS (On-Premise D365)

```csharp
// Program.cs - Fluent Builder for ADFS
builder.Services.AddD365ODataClient(d365 => 
{
    d365.UseADFS()
        .WithTokenEndpoint("https://fs.your-company.com/adfs/oauth2/token")
        .WithClientId("your-client-id")
        .WithClientSecret("your-client-secret")
        .WithResource("https://ax.your-company.com")
        .WithOrganizationUrl("https://ax.your-company.com/namespaces/AXSF/");
});
```

```json
// appsettings.json for ADFS
{
  "D365OnPrem": {
    "TenantId": "adfs",
    "TokenEndpoint": "https://fs.your-company.com/adfs/oauth2/token",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "Resource": "https://ax.your-company.com",
    "OrganizationUrl": "https://ax.your-company.com/namespaces/AXSF/",
    "GrantType": "client_credentials"
  }
}
```

```csharp
// From configuration (auto-detects ADFS when TenantId="adfs" or TokenEndpoint is set)
builder.Services.AddD365ODataClient(builder.Configuration, "D365OnPrem");
```

---
 
 ### Option 3: Microsoft Dataverse (CRM / Power Platform)
 
 ```csharp
 // Program.cs - Fluent Builder for Dataverse
 builder.Services.AddD365ODataClient(D365ServiceScope.Dataverse, d365 => 
 {
     d365.WithClientId("your-client-id")
         .WithClientSecret("your-client-secret")
         .WithTenantId("your-tenant-id")
         .WithResource("https://org.api.crm5.dynamics.com")
         .WithScope("https://org.api.crm5.dynamics.com/.default"); // Optional explicit scope
 });
 ```
 
 ```json
 // appsettings.json
 {
   "DataverseConfigs": {
     "ClientId": "your-client-id",
     "ClientSecret": "your-client-secret",
     "TenantId": "your-tenant-id",
     "Resource": "https://org.api.crm5.dynamics.com",
     "TokenEndpoint": "https://login.microsoftonline.com/..." // Optional
   }
 }
 ```
 
 ```csharp
 // From configuration
 builder.Services.AddD365ODataClient(
     D365ServiceScope.Dataverse, 
     builder.Configuration, 
     "DataverseConfigs");
 ```
 
 ---

### Option 4: Multiple D365 Sources (Cloud + On-Premise)

```csharp
// Program.cs - Named Services for multiple D365 sources
builder.Services.AddD365ODataClient(D365ServiceScope.Cloud, d365 => 
{
    d365.UseAzureAD()
        .WithClientId("cloud-client-id")
        .WithClientSecret("cloud-secret")
        .WithTenantId("cloud-tenant-id")
        .WithResource("https://cloud.operations.dynamics.com");
});

builder.Services.AddD365ODataClient(D365ServiceScope.OnPrem, d365 => 
{
    d365.UseADFS()
        .WithTokenEndpoint("https://fs.company.com/adfs/oauth2/token")
        .WithClientId("onprem-client-id")
        .WithClientSecret("onprem-secret")
        .WithResource("https://ax.company.com")
        .WithOrganizationUrl("https://ax.company.com/namespaces/AXSF/");
});
```

```csharp
// Or from configuration with named sections
builder.Services.AddD365ODataClient(D365ServiceScope.Cloud, builder.Configuration, "D365Cloud");
builder.Services.AddD365ODataClient(D365ServiceScope.OnPrem, builder.Configuration, "D365OnPrem");
```

---

## Usage

### Single Source - Inject ID365Service

```csharp
public class ProductService
{
    private readonly ID365Service _d365;
    
    public ProductService(ID365Service d365) => _d365 = d365;
    
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .Where(p => p.ItemNumber.StartsWith("A"))
            .Select(p => new { p.ItemNumber, p.ProductName })
            .ToListAsync();
    }
}
```

### Multiple Sources - Inject ID365ServiceFactory

```csharp
public class SyncService
{
    private readonly ID365ServiceFactory _d365Factory;
    
    public SyncService(ID365ServiceFactory d365Factory) => _d365Factory = d365Factory;
    
    public async Task SyncDataAsync()
    {
        // Get specific D365 source by name
        var cloudD365 = _d365Factory.GetService("Cloud");
        var onPremD365 = _d365Factory.GetService("OnPrem");
        
        // Query from Cloud
        var cloudProducts = await cloudD365
            .Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .ToListAsync();
        
        // Query from On-Premise
        var onPremOrders = await onPremD365
            .Entity<SalesOrder>("SalesOrderHeadersV2")
            .CrossCompany()
            .ToListAsync();
    }
}
```

---

## CRUD Operations

```csharp
// Create
await _d365.Entity<Customer>("CustomersV3").AddAsync(newCustomer);

// Read
var customers = await _d365.Entity<Customer>("CustomersV3")
    .CrossCompany()
    .Where(c => c.CustomerAccount == "CUST001")
    .ToListAsync();

// Update
await _d365.Entity<Customer>("CustomersV3")
    .AddIdentity("CustomerAccount", "CUST001")
    .AddIdentity("dataAreaId", "usmf")
    .CrossCompany()
    .UpdateAsync(new { CustomerName = "Updated Name" });

// Delete
await _d365.Entity<Customer>("CustomersV3")
    .AddIdentity("CustomerAccount", "CUST001")
    .AddIdentity("dataAreaId", "usmf")
    .CrossCompany()
    .DeleteAsync();
```

---

## API Reference

### Query Methods

| Method | Description |
|--------|-------------|
| `Entity<T>(string entityName)` | Start a query for the specified entity |
| `CrossCompany()` | Enable cross-company query |
| `Where(Expression<Func<T, bool>>)` | Filter using LINQ expression |
| `Select(Expression<Func<T, object>>)` | Select specific properties |
| `OrderBy(string property)` | Sort ascending |
| `Skip(int count)` | Skip N records |
| `Take(int count)` | Take N records |
| `AddIdentity(string key, object value)` | Add entity key for updates |
| `PageSize(int size)` | Set page size for pagination |

### Execute Methods

| Method | Description |
|--------|-------------|
| `ToListAsync()` | Execute query and return list |
| `FirstOrDefaultAsync()` | Execute query and return first or null |
| `CountAsync()` | Get count of matching records |
| `AddAsync(T entity)` | Create new record |
| `UpdateAsync(T entity)` | Update existing record |
| `DeleteAsync()` | Delete record |

---

## Requirements

- .NET 8.0 or later
- Microsoft Dynamics 365 Finance & Operations
- **Azure AD**: App Registration with D365 F&O API permissions
- **ADFS**: Native Application registered in ADFS

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
