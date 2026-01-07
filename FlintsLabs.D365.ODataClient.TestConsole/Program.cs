using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Console.WriteLine("=== FlintsLabs.D365.ODataClient Test Console ===\n");
Console.WriteLine("Choose Test Mode:");
Console.WriteLine("1. Single Source - Cloud (ID365Service)");
Console.WriteLine("2. Single Source - OnPrem (ID365Service)");
Console.WriteLine("3. Multi-Source (ID365ServiceFactory)");
Console.Write("\nEnter choice (1, 2, or 3): ");

var choice = Console.ReadLine();

switch (choice)
{
    case "1":
        await TestSingleSourceAsync(args, "D365Configs_OnCloud", "Cloud");
        break;
    case "2":
        await TestSingleSourceAsync(args, "D365Configs_OnPrem", "OnPrem");
        break;
    case "3":
        await TestMultiSourceAsync(args);
        break;
    default:
        Console.WriteLine("Invalid choice. Running Cloud test by default...");
        await TestSingleSourceAsync(args, "D365Configs_OnCloud", "Cloud");
        break;
}

// ============================================
// Test: Single Source (ID365Service)
// ============================================
async Task TestSingleSourceAsync(string[] args, string configSection, string envName)
{
    Console.WriteLine($"\n=== Test Mode: Single Source - {envName} (ID365Service) ===\n");
    
    // Step 1: Build host with single D365 source
    var builder = Host.CreateApplicationBuilder(args);
    
    // Register D365 client WITHOUT scope (uses "Default" internally)
    builder.Services.AddD365ODataClient(builder.Configuration, configSection);
    
    var host = builder.Build();
    
    // Step 2: Get ID365Service directly from DI (no factory needed!)
    var d365 = host.Services.GetRequiredService<ID365Service>();
    
    Console.WriteLine("✓ ID365Service injected directly (no factory)");
    Console.WriteLine($"✓ Config: {configSection}\n");
    
    // ==============================
    // Test: Count LegalEntities
    // ==============================
    Console.WriteLine("--- [Test 1] Count LegalEntities ---");
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
    
    // ==============================
    // Test: Query ReleasedProductsV2
    // ==============================
    Console.WriteLine("\n--- [Test 2] Query ReleasedProductsV2 (Top 3) ---");
    try
    {
        var products = await d365.Entity<dynamic>("ReleasedProductsV2")
            .CrossCompany()
            .Take(3)
            .ToListAsync();
        
        Console.WriteLine($"✓ Fetched {products.Count} products");
        foreach (var p in products)
        {
            Console.WriteLine($"   - {p}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
    }
    
    // ==============================
    // Test: Query CustomersV3
    // ==============================
    Console.WriteLine("\n--- [Test 3] Query CustomersV3 (Top 3) ---");
    try
    {
        var customers = await d365.Entity<dynamic>("CustomersV3")
            .CrossCompany()
            .Take(3)
            .ToListAsync();
        
        Console.WriteLine($"✓ Fetched {customers.Count} customers");
        foreach (var c in customers)
        {
            Console.WriteLine($"   - {c}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
    }
    
    // ==============================
    // Test: Query VendorsV2
    // ==============================
    Console.WriteLine("\n--- [Test 4] Query VendorsV2 (Top 3) ---");
    try
    {
        var vendors = await d365.Entity<dynamic>("VendorsV2")
            .CrossCompany()
            .Take(3)
            .ToListAsync();
        
        Console.WriteLine($"✓ Fetched {vendors.Count} vendors");
        foreach (var v in vendors)
        {
            Console.WriteLine($"   - {v}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
    }
    
    // ==============================
    // Test: Query SalesOrderHeadersV2
    // ==============================
    Console.WriteLine("\n--- [Test 5] Query SalesOrderHeadersV2 (Top 3) ---");
    try
    {
        var orders = await d365.Entity<dynamic>("SalesOrderHeadersV2")
            .CrossCompany()
            .Take(3)
            .ToListAsync();
        
        Console.WriteLine($"✓ Fetched {orders.Count} sales orders");
        foreach (var o in orders)
        {
            Console.WriteLine($"   - {o}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
    }
    
    Console.WriteLine($"\n=== Single Source Test ({envName}) Complete ===");
}

// ============================================
// Test: Multi-Source (ID365ServiceFactory)
// ============================================
async Task TestMultiSourceAsync(string[] args)
{
    Console.WriteLine("\n=== Test Mode: Multi-Source (ID365ServiceFactory) ===\n");
    
    // Step 1: Build host with MULTIPLE D365 sources
    var builder = Host.CreateApplicationBuilder(args);
    
    // Register multiple D365 clients with Enum names
    builder.Services.AddD365ODataClient(
        D365ServiceScope.Cloud,
        builder.Configuration, 
        "D365Configs_OnCloud");
    
    builder.Services.AddD365ODataClient(
        D365ServiceScope.OnPrem,
        builder.Configuration, 
        "D365Configs_OnPrem");
    
    var host = builder.Build();
    
    // Step 2: Get ID365ServiceFactory from DI
    var factory = host.Services.GetRequiredService<ID365ServiceFactory>();
    
    Console.WriteLine("✓ ID365ServiceFactory injected");
    Console.WriteLine("✓ Registered: Cloud (Sandbox) + OnPrem (ADFS)\n");
    
    // ==============================
    // Test: Cloud (Sandbox)
    // ==============================
    Console.WriteLine("--- [Cloud] Query LegalEntities ---");
    try
    {
        var cloudD365 = factory.GetService("Cloud");
        var count = await cloudD365.Entity<dynamic>("LegalEntities")
            .CrossCompany()
            .CountAsync();
        
        Console.WriteLine($"✓ Cloud LegalEntities: {count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Cloud Error: {ex.Message}");
    }
    
    Console.WriteLine("\n--- [Cloud] Query CustomersV3 (Top 2) ---");
    try
    {
        var cloudD365 = factory.GetService("Cloud");
        var customers = await cloudD365.Entity<dynamic>("CustomersV3")
            .CrossCompany()
            .Take(2)
            .ToListAsync();
        
        Console.WriteLine($"✓ Cloud Customers: {customers.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Cloud Error: {ex.Message}");
    }
    
    // ==============================
    // Test: OnPrem (ADFS)
    // ==============================
    Console.WriteLine("\n--- [OnPrem] Query LegalEntities ---");
    try
    {
        var onPremD365 = factory.GetService("OnPrem");
        var count = await onPremD365.Entity<dynamic>("LegalEntities")
            .CrossCompany()
            .CountAsync();
        
        Console.WriteLine($"✓ OnPrem LegalEntities: {count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ OnPrem Error: {ex.Message}");
    }
    
    Console.WriteLine("\n--- [OnPrem] Query VendorsV2 (Top 2) ---");
    try
    {
        var onPremD365 = factory.GetService("OnPrem");
        var vendors = await onPremD365.Entity<dynamic>("VendorsV2")
            .CrossCompany()
            .Take(2)
            .ToListAsync();
        
        Console.WriteLine($"✓ OnPrem Vendors: {vendors.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ OnPrem Error: {ex.Message}");
    }
    
    Console.WriteLine("\n=== Multi-Source Test Complete ===");
}
