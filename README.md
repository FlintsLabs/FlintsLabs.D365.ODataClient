# FlintsLabs.D365.ODataClient

A fluent OData client for Microsoft Dynamics 365 Finance & Operations.


![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512bd4)
![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512bd4)
[![NuGet](https://img.shields.io/nuget/v/FlintsLabs.D365.ODataClient.svg)](https://www.nuget.org/packages/FlintsLabs.D365.ODataClient)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- 🔗 **Fluent API** - Chainable query builder with IntelliSense support
- 🔍 **LINQ Support** - Write queries using lambda expressions
- ➕ **Expand Support** - Easily expand navigation properties (`query.Expand("Nav")` or `query.Expand(x => x.Nav)`)
- 📨 **Custom Headers** - Add custom headers like `Prefer` to requests
- 🏢 **Cross-Company** - Query across legal entities
- 🔐 **Multi-Auth Support** - Azure AD (Cloud), ADFS (On-Premise), and **Dataverse**
- 📦 **CRUD Operations** - Full Create, Read, Update, Delete support
- 🌐 **Multi-Source** - Connect to multiple D365 instances (F&O, Dataverse) simultaneously

## Table of Contents

- [Installation](#installation)
- [Configuration](#configuration)
  - [Option 1: Azure AD (Cloud)](#option-1-azure-ad-cloud-d365)
  - [Option 2: ADFS (On-Premise)](#option-2-adfs-on-premise-d365)
  - [Option 3: Dataverse](#option-3-microsoft-dataverse-crm--power-platform)
  - [Option 4: Multiple Sources](#option-4-multiple-d365-sources-cloud--on-premise)
- [Usage in ASP.NET Core](#usage-in-aspnet-core)
  - [Quick Start (Single Source)](#quick-start-single-d365-source)
  - [Advanced (Multiple Sources)](#advanced-multiple-d365-sources)
  - [Naming Convention](#naming-convention-summary)
- [CRUD Operations](#crud-operations)
- [API Reference](#api-reference)
- [Requirements](#requirements)

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
         .WithOrganizationUrl("https://org.api.crm5.dynamics.com/api/data/v9.2/")
         .WithScope("https://org.api.crm5.dynamics.com/.default")
         .WithBooleanFormatting(D365BooleanFormatting.Literal);
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
     "OrganizationUrl": "https://org.api.crm5.dynamics.com/api/data/v9.2/",
     "Scope": "https://org.api.crm5.dynamics.com/.default",
     "BooleanFormatting": "Literal"
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

> [!IMPORTANT]
> **Dataverse requires both `Resource` and `OrganizationUrl`:**
> - **Resource** = Used for authentication token (base domain only)
> - **OrganizationUrl** = Used as API base URL (includes `/api/data/v9.2/`)
> 
> If omitted, the library uses `Resource` + `/data/` which is incorrect for Dataverse.
 
 > [!TIP]
 > **Boolean Formatting:**
 > Dataverse uses standard `true`/`false` for booleans, while D365 F&O uses `NoYes` enum.
 > Use `WithBooleanFormatting(D365BooleanFormatting.Literal)` or set `"BooleanFormatting": "Literal"` in config for Dataverse.
 
 > [!NOTE]
 > **Nullable Booleans (`bool?`):**
 > When using `GetValueOrDefault()`, the library translates it to check against `true`.
 > 
 > | Expression | C# Value (`null`) | C# Value (`false`) | C# Value (`true`) |
 > |------------|-------------------|-------------------|-------------------|
 > | `x.Prop.GetValueOrDefault()` | `false` (Excludes) | `false` (Excludes) | `true` (Includes) |
 > | `!x.Prop.GetValueOrDefault()` | `true` (Includes) | `true` (Includes) | `false` (Excludes) |
 > 
 > *Note: `null` treats as `false`*

 
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

## Usage in ASP.NET Core

### Quick Start (Single D365 Source)

Use this pattern when connecting to **one D365 instance only**.

**Step 1: Configure `appsettings.json`**
```json
{
  "D365": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "TenantId": "your-tenant-id",
    "Resource": "https://your-org.operations.dynamics.com"
  }
}
```

**Step 2: Register in `Program.cs`**
```csharp
// Register D365 client (no name = "Default")
builder.Services.AddD365ODataClient(builder.Configuration, "D365");
```

**Step 3: Inject `ID365Service` in Controller**
```csharp
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly ID365Service _d365;

    // DI will inject ID365Service automatically
    public ProductsController(ID365Service d365)
    {
        _d365 = d365;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts()
    {
        var products = await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .Where(p => p.ItemNumber.StartsWith("A"))
            .Take(10)
            .ToListAsync();

        return Ok(products);
    }
    
    // IN clause (multiple values) - auto-generates OR filter
    [HttpGet("by-codes")]
    public async Task<IActionResult> GetProductsByCodes([FromQuery] string[] codes)
    {
        var products = await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .Where(p => codes.Contains(p.ItemNumber))  // -> (ItemNumber eq 'A001' or ItemNumber eq 'A002' ...)
            .ToListAsync();

        return Ok(products);
    }
}
```

---

### Type-Safe Entity Names (User-Defined Enum)

Instead of using magic strings for entity names, you can define your own enum for **type-safety** and **IntelliSense support**.

#### Step 1: Define Your Entity Enum

```csharp
using System.ComponentModel;

namespace MyApp.D365;

/// <summary>
/// Custom D365 Entity names for type-safe queries.
/// Use [Description] attribute to map to actual D365 entity name.
/// If no [Description], the enum member name is used.
/// </summary>
public enum D365Entity
{
    // CUSTOMERS & VENDORS
    [Description("CustomersV3")]
    Customer,
    
    [Description("VendorsV2")]
    Vendor,
    
    // PRODUCTS
    [Description("ReleasedProductsV2")]
    Product,
    
    [Description("InventItemBarcodes")]
    Barcode,
    
    // ORDERS
    [Description("SalesOrderHeadersV2")]
    SalesOrderHeader,
    
    [Description("SalesOrderLines")]
    SalesOrderLine,
    
    [Description("PurchaseOrderHeadersV2")]
    PurchaseOrderHeader,
    
    // FINANCE
    [Description("LedgerJournalHeaders")]
    JournalHeader,
    
    // COMMON (no [Description] - uses enum name directly)
    LegalEntities,
    Companies,
    Currencies
}
```

#### Step 2: Use Enum in Queries

```csharp
// BEFORE: Magic string (error-prone)
var customers = await d365.Entity<Customer>("CustomersV3").ToListAsync();

// AFTER: Type-safe enum (recommended)
var customers = await d365.Entity<Customer>(D365Entity.Customer).ToListAsync();

// Works with all query methods
var products = await d365.Entity<Product>(D365Entity.Product)
    .CrossCompany()
    .Where(p => p.IsActive == true)
    .Take(100)
    .ToListAsync();
```

#### Resolution Priority

| Priority | Source | Example |
|----------|--------|---------|
| 1️⃣ | `[Description("...")]` | `[Description("CustomersV3")]` → `"CustomersV3"` |
| 2️⃣ | Enum member name | `LegalEntities` → `"LegalEntities"` |

#### Migration Guide (String → Enum)

**Before (v1.2.15 and earlier):**
```csharp
public class ProductController : ControllerBase
{
    private readonly ID365Service _d365;
    
    public async Task<IActionResult> GetProducts()
    {
        var products = await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .ToListAsync();
        return Ok(products);
    }
}
```

**After (v1.2.16+):**
```csharp
// 1. Create enum file: Enums/D365Entity.cs
public enum D365Entity
{
    [Description("ReleasedProductsV2")]
    Product
}

// 2. Update controller to use enum
public class ProductController : ControllerBase
{
    private readonly ID365Service _d365;
    
    public async Task<IActionResult> GetProducts()
    {
        var products = await _d365.Entity<Product>(D365Entity.Product)  // <- Changed!
            .CrossCompany()
            .ToListAsync();
        return Ok(products);
    }
}
```

#### Benefits

- ✅ **No typos** - Compiler catches invalid entity names
- ✅ **IntelliSense** - Auto-complete entity names
- ✅ **Centralized** - All entity names in one file
- ✅ **Refactor-safe** - Rename enum updates all usages
- ✅ **Documentation** - XML comments on enum members

#### Performance

> [!NOTE]
> **Internal Caching**: The library automatically caches enum-to-string lookups using `ConcurrentDictionary`.
> The first call uses reflection, subsequent calls are O(1) dictionary lookups.

| Method | First Call | Subsequent Calls | Type-Safety |
|--------|------------|------------------|-------------|
| **String** `.Entity<T>("CustomersV3")` | ⚡ Fast | ⚡ Fast | ❌ |
| **Enum** `.Entity<T>(D365Entity.Customer)` | 🐢 Reflection | ⚡ Cached | ✅ |

**Recommendation**: Use **Enum** for most cases (type-safety + cached performance). Use **String** only in extremely high-frequency loops where every nanosecond matters.

#### Multiple Enum Types

You can organize entities into **multiple enum types** (e.g., by module). Each enum type is cached separately:

```csharp
// Sales module entities
public enum SalesEntity
{
    [Description("SalesOrderHeadersV2")]
    SalesOrder,
    
    [Description("SalesOrderLines")]
    SalesOrderLine
}

// Purchasing module entities
public enum PurchaseEntity
{
    [Description("PurchaseOrderHeadersV2")]
    PurchaseOrder
}

// Usage - both work correctly, no conflicts
var orders = await d365.Entity<SO>(SalesEntity.SalesOrder).ToListAsync();
var pos = await d365.Entity<PO>(PurchaseEntity.PurchaseOrder).ToListAsync();
```

> [!TIP]
> Adding new enum values or creating new enum types requires no configuration - the library handles caching automatically.

---

### Advanced (Multiple D365 Sources)

Use this pattern when connecting to **multiple D365 instances** (e.g., Cloud + On-Premise, Production + Sandbox).

**Step 1: Configure `appsettings.json`** with multiple sections
```json
{
  "D365Cloud": {
    "ClientId": "cloud-client-id",
    "ClientSecret": "cloud-secret",
    "TenantId": "cloud-tenant-id",
    "Resource": "https://cloud.operations.dynamics.com"
  },
  "D365OnPrem": {
    "TenantId": "adfs",
    "TokenEndpoint": "https://fs.company.com/adfs/oauth2/token",
    "ClientId": "onprem-client-id",
    "ClientSecret": "onprem-secret",
    "Resource": "https://ax.company.com",
    "OrganizationUrl": "https://ax.company.com/namespaces/AXSF/"
  }
}
```

**Step 2: Register with Names in `Program.cs`**

> ⚠️ **Important**: The name you use here must match what you use in `GetService("name")` later!

```csharp
// Option A: Use Enum (recommended - prevents typos)
builder.Services.AddD365ODataClient(D365ServiceScope.Cloud, builder.Configuration, "D365Cloud");
builder.Services.AddD365ODataClient(D365ServiceScope.OnPrem, builder.Configuration, "D365OnPrem");

// Option B: Use custom string names (flexible)
builder.Services.AddD365ODataClient("Org1-Cloud", builder.Configuration, "D365Cloud");
builder.Services.AddD365ODataClient("Org2-OnPrem", builder.Configuration, "D365OnPrem");
```

> [!WARNING]  
> **Each client must have a unique name!**  
> Registering the same name twice will throw an `InvalidOperationException` at startup:
> ```
> D365 client 'Cloud' is already registered. Use a unique name for each client.
> ```

**Step 3: Inject `ID365ServiceFactory` in Controller**

```csharp
[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ID365ServiceFactory _d365Factory;

    // DI will inject the factory (not individual services)
    public SyncController(ID365ServiceFactory d365Factory)
    {
        _d365Factory = d365Factory;
    }

    [HttpGet("cloud-products")]
    public async Task<IActionResult> GetFromCloud()
    {
        // Get service by the SAME name used in Program.cs
        var d365 = _d365Factory.GetService("Cloud");  // Matches D365ServiceScope.Cloud
        
        var products = await d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .Take(10)
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("onprem-orders")]
    public async Task<IActionResult> GetFromOnPrem()
    {
        var d365 = _d365Factory.GetService("OnPrem");  // Matches D365ServiceScope.OnPrem
        
        var orders = await d365.Entity<SalesOrder>("SalesOrderHeadersV2")
            .CrossCompany()
            .Take(10)
            .ToListAsync();

        return Ok(orders);
    }
}
```

---

### Naming Convention Summary

| Registration Method | GetService() Call | Notes |
|---------------------|-------------------|-------|
| `AddD365ODataClient(config, "D365")` | `GetService()` or `GetService("Default")` | Default name |
| `AddD365ODataClient(D365ServiceScope.Cloud, ...)` | `GetService("Cloud")` | Enum → String |
| `AddD365ODataClient(D365ServiceScope.OnPrem, ...)` | `GetService("OnPrem")` | Enum → String |
| `AddD365ODataClient("MyCustomName", ...)` | `GetService("MyCustomName")` | Custom string |

> 💡 **Tip**: Use `D365ServiceScope` enum to prevent typos. The enum values are: `Default`, `Cloud`, `OnPrem`, `Dataverse`

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
| `OrderBy(x => x.Property)` | Sort ascending using LINQ expression |
| `OrderByDescending(x => x.Property)` | Sort descending using LINQ expression |
| `ThenBy(x => x.Property)` | Secondary sort ascending |
| `ThenByDescending(x => x.Property)` | Secondary sort descending |
| `OrderBy(string property, bool asc)` | Sort by property name (legacy) |
| `Skip(int count)` | Skip N records |
| `Take(int count)` | Take N records |
| `AddIdentity(string key, object value)` | Add entity key for updates |
| `PageSize(int size)` | Set page size for pagination |
| `Expand(string nav)` | Expand navigation property |
| `Expand(Expression<Func<T, object>>)` | Expand navigation property (lambda) |
| `AddHeader(string key, string value)` | Add custom request header |

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

## Development

### Running Tests

This project includes both **Integration Tests** (xUnit) and an interactive **Test Console**.

1.  **Configuration**:
    - Rename `appsettings.example.json` to `appsettings.json` in the test project.
    - Update with your real D365 credentials (this file is git-ignored).

2.  **Run xUnit Tests** (Automated):
    ```bash
    dotnet test
    ```

3.  **Run Test Console** (Interactive):
    ```bash
    cd FlintsLabs.D365.ODataClient.TestConsole
    dotnet run
    ```

### Logging

All HTTP requests are logged with full URLs. Enable Debug level to see request bodies:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

**Example output:**
```
info: D365 GET: https://org1.../data/ReleasedProductsV2?cross-company=true&$top=3
dbug: Request Body: {"SalesOrderNumber":"SO-001",...}
```

### Verification (.NET 10)

This library is verified to support **.NET 10**. To verify compatibility:

```bash
dotnet test -f net10.0
```

---

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
