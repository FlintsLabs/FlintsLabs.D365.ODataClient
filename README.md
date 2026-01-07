# FlintsLabs.D365.ODataClient

A fluent OData client for Microsoft Dynamics 365 Finance & Operations.

[![NuGet](https://img.shields.io/nuget/v/FlintsLabs.D365.ODataClient.svg)](https://www.nuget.org/packages/FlintsLabs.D365.ODataClient)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- 🔗 **Fluent API** - Chainable query builder with IntelliSense support
- 🔍 **LINQ Support** - Write queries using lambda expressions
- 🏢 **Cross-Company** - Query across legal entities
- 🔐 **Auto Token Management** - Automatic token refresh via Microsoft Entra ID
- 📦 **CRUD Operations** - Full Create, Read, Update, Delete support

## Installation

```bash
dotnet add package FlintsLabs.D365.ODataClient
```

## Quick Start

### 1. Configure Services

```csharp
// Program.cs
builder.Services.AddD365ODataClient(options =>
{
    options.ClientId = builder.Configuration["D365:ClientId"];
    options.ClientSecret = builder.Configuration["D365:ClientSecret"];
    options.TenantId = builder.Configuration["D365:TenantId"];
    options.Resource = builder.Configuration["D365:Resource"]; // e.g. https://your-org.operations.dynamics.com
});
```

### 2. Add Configuration

```json
// appsettings.json
{
  "D365": {
    "ClientId": "your-app-registration-client-id",
    "ClientSecret": "your-client-secret",
    "TenantId": "your-tenant-id",
    "Resource": "https://your-org.operations.dynamics.com"
  }
}
```

### 3. Use in Your Code

```csharp
public class ProductService
{
    private readonly ID365Service _d365;
    
    public ProductService(ID365Service d365) => _d365 = d365;
    
    // Query with LINQ
    public async Task<List<Product>> GetProductsAsync()
    {
        return await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .Where(p => p.ItemNumber.StartsWith("A"))
            .Select(p => new { p.ItemNumber, p.ProductName })
            .ToListAsync();
    }
    
    // Update with Identity
    public async Task<string> UpdateProductAsync(Product product)
    {
        return await _d365.Entity<Product>("ReleasedProductsV2")
            .CrossCompany()
            .AddIdentity("dataAreaId", product.DataAreaId)
            .AddIdentity("ItemNumber", product.ItemNumber)
            .UpdateAsync(product);
    }
    
    // Create new record
    public async Task<string> CreateProductAsync(Product product)
    {
        return await _d365.Entity<Product>("ReleasedProductsV2")
            .AddAsync(product);
    }
    
    // Delete record
    public async Task<string> DeleteProductAsync(string dataAreaId, string itemNumber)
    {
        return await _d365.Entity<Product>("ReleasedProductsV2")
            .AddIdentity("dataAreaId", dataAreaId)
            .AddIdentity("ItemNumber", itemNumber)
            .DeleteAsync();
    }
}
```

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

## Requirements

- .NET 8.0 or later
- Microsoft Dynamics 365 Finance & Operations
- Azure App Registration with appropriate D365 permissions

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
