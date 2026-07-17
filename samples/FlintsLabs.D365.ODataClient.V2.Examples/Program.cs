using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.V2.Examples.Models;
using Microsoft.Extensions.DependencyInjection;

if (!string.Equals(
        Environment.GetEnvironmentVariable("D365_RUN_SAMPLE"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Set D365_RUN_SAMPLE=true and the documented environment variables to run an example.");
    return;
}

var organizationUrl = RequireEnvironmentVariable("D365_ORGANIZATION_URL");
var resource = Environment.GetEnvironmentVariable("D365_RESOURCE")
    ?? new Uri(organizationUrl).GetLeftPart(UriPartial.Authority);
var entityName = Environment.GetEnvironmentVariable("D365_ENTITY_NAME") ?? "rvl_egrheads";

var services = new ServiceCollection();
services.AddD365ODataClient("Sales", builder => builder
    .UseAzureAD()
    .WithOrganizationUrl(organizationUrl)
    .WithResource(resource)
    .WithTenantId(RequireEnvironmentVariable("D365_TENANT_ID"))
    .WithClientId(RequireEnvironmentVariable("D365_CLIENT_ID"))
    .WithClientSecret(RequireEnvironmentVariable("D365_CLIENT_SECRET"))
    .WithScope(RequireEnvironmentVariable("D365_SCOPE"))
    .ConfigureRetry(retry =>
    {
        retry.MaxReadRetries = 2;
        retry.BaseDelay = TimeSpan.FromMilliseconds(250);
        retry.MaxDelay = TimeSpan.FromSeconds(10);
    }));

await using var provider = services.BuildServiceProvider();
var injectedClient = provider.GetRequiredService<ID365Client>();
var namedClient = provider.GetRequiredService<ID365ClientFactory>().GetClient("Sales");
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var action = (Environment.GetEnvironmentVariable("D365_SAMPLE_ACTION") ?? "read")
    .ToLowerInvariant();
switch (action)
{
    case "read":
        await ReadAsync(injectedClient, entityName, GetHeadId(), cancellation.Token);
        break;
    case "update":
        await UpdateAsync(namedClient, entityName, GetHeadId(), cancellation.Token);
        break;
    case "delete":
        await DeleteAsync(namedClient, entityName, GetHeadId(), cancellation.Token);
        break;
    case "create":
        await CreateAsync(namedClient, entityName, cancellation.Token);
        break;
    case "raw":
        await RawAsync(namedClient, entityName, cancellation.Token);
        break;
    default:
        throw new InvalidOperationException(
            "D365_SAMPLE_ACTION must be read, update, delete, create, or raw.");
}

static async Task ReadAsync(
    ID365Client client,
    string entityName,
    Guid headId,
    CancellationToken cancellationToken)
{
    var existing = await client
        .Entity<EgrHead>(entityName)
        .Where(head => head.Id == headId)
        .FirstOrDefaultAsync(cancellationToken);

    Console.WriteLine(existing is null
        ? "The successful query returned no matching row."
        : $"Found {existing.Id}: {existing.Name}");
}

static async Task UpdateAsync(
    ID365Client client,
    string entityName,
    Guid headId,
    CancellationToken cancellationToken)
{
    try
    {
        var response = await client
            .Entity<EgrHead>(entityName)
            .Where(head => head.Id == headId)
            .UpdateAsync(new { rvl_wmsstatus = false }, cancellationToken);
        Console.WriteLine($"PATCH completed with HTTP {(int)response.StatusCode}.");
    }
    catch (D365TransportException exception)
        when (exception.MutationOutcome == D365MutationOutcome.Unknown)
    {
        Console.WriteLine("PATCH outcome is unknown. Reconcile by the exact key before retrying.");
    }
    catch (D365OperationCanceledException exception)
        when (exception.MutationOutcome == D365MutationOutcome.Unknown)
    {
        Console.WriteLine("PATCH was canceled after send. Reconcile by the exact key before retrying.");
    }
}

static async Task DeleteAsync(
    ID365Client client,
    string entityName,
    Guid headId,
    CancellationToken cancellationToken)
{
    var response = await client
        .Entity<EgrHead>(entityName)
        .Where(head => head.Id == headId)
        .DeleteAsync(cancellationToken);
    Console.WriteLine($"DELETE completed with HTTP {(int)response.StatusCode}.");
}

static async Task CreateAsync(
    ID365Client client,
    string entityName,
    CancellationToken cancellationToken)
{
    var response = await client
        .Entity<EgrHead>(entityName)
        .AddHeader("Prefer", "return=representation")
        .AddAsync<EgrHead>(
            new
            {
                rvl_name = $"v2-sample-{Guid.NewGuid():N}",
                rvl_wmsstatus = false
            },
            cancellationToken);

    Console.WriteLine(
        $"POST completed with HTTP {(int)response.StatusCode}; created ID={response.Value?.Id}.");
}

static async Task RawAsync(
    ID365Client client,
    string entityName,
    CancellationToken cancellationToken)
{
    var response = await client.SendAsync(
        HttpMethod.Get,
        $"{entityName}?$top=1",
        cancellationToken: cancellationToken);

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        Console.WriteLine("Raw request received a real HTTP 404 response.");
        return;
    }

    Console.WriteLine(
        $"Raw request HTTP {(int)response.StatusCode}; request ID={response.RequestId ?? "n/a"}.");
}

static Guid GetHeadId()
{
    return Guid.Parse(RequireEnvironmentVariable("D365_HEAD_ID"));
}

static string RequireEnvironmentVariable(string name)
{
    return Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable {name} is required.");
}
