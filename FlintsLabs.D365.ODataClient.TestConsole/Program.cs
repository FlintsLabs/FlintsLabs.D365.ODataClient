using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("=== FlintsLabs.D365.ODataClient Test Console ===\n");

// Build host with DI
var builder = Host.CreateApplicationBuilder(args);

// Add D365 OData Client from configuration
builder.Services.AddD365ODataClient(
    D365ServiceScope.Cloud,
    builder.Configuration,
    "D365Configs");

var host = builder.Build();

// Get service factory and run test
var factory = host.Services.GetRequiredService<ID365ServiceFactory>();
var d365 = factory.GetService("Cloud");

Console.WriteLine("✓ Service created successfully");

// Test 1: Query ReleasedProductsV2 (Common entity in F&O)
Console.WriteLine("\n--- Test 1: Query ReleasedProductsV2 ---");
try
{
    var products = await d365.Entity<dynamic>("ReleasedProductsV2")
        .CrossCompany()
        .Take(5)
        .ToListAsync();

    Console.WriteLine($"✓ Fetched {products.Count} products");
    
    foreach (var product in products)
    {
        Console.WriteLine($"  - {product}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
}

// Test 2: Query CustomersV3
Console.WriteLine("\n--- Test 2: Query CustomersV3 ---");
try
{
    var customers = await d365.Entity<dynamic>("CustomersV3")
        .CrossCompany()
        .Take(3)
        .ToListAsync();

    Console.WriteLine($"✓ Fetched {customers.Count} customers");
    
    foreach (var customer in customers)
    {
        Console.WriteLine($"  - {customer}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
}

// Test 3: Count test
Console.WriteLine("\n--- Test 3: Count LegalEntities ---");
try
{
    var count = await d365.Entity<dynamic>("LegalEntities")
        .CrossCompany()
        .CountAsync();

    Console.WriteLine($"✓ Total LegalEntities: {count}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error: {ex.Message}");
}

Console.WriteLine("\n=== Test Complete ===");
