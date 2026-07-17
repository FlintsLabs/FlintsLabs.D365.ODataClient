# Authentication and Parallelism

Version 2 keeps authentication inside each named `ID365Client` and shares its token provider for all calls made through the same service provider/client name.

## Azure AD / Microsoft Entra ID

The cloud flow uses MSAL confidential-client credentials:

```csharp
services.AddD365ODataClient("Finance", d365 => d365
    .UseAzureAD()
    .WithResource("https://contoso.operations.dynamics.com")
    .WithTenantId(tenantId)
    .WithClientId(clientId)
    .WithClientSecret(clientSecret));
```

Scope selection:

- Explicit `WithScope(scope)` wins.
- Otherwise the client uses `Resource.TrimEnd('/') + "/.default"`.

Dataverse example:

```csharp
services.AddD365ODataClient("Sales", d365 => d365
    .UseAzureAD()
    .WithOrganizationUrl(
        "https://contoso.api.crm5.dynamics.com/api/data/v9.2")
    .WithResource("https://contoso.api.crm5.dynamics.com")
    .WithScope("https://contoso.api.crm5.dynamics.com/.default")
    .WithTenantId(tenantId)
    .WithClientId(clientId)
    .WithClientSecret(clientSecret));
```

## ADFS

The retained on-premises flow posts form fields to the configured token endpoint:

- `tenant_id`
- `client_id`
- `client_secret`
- `resource`
- `grant_type`

```csharp
services.AddD365ODataClient("OnPrem", d365 => d365
    .UseADFS()
    .WithTokenEndpoint("https://fs.contoso.local/adfs/oauth2/token")
    .WithTenantId("adfs")
    .WithClientId(clientId)
    .WithClientSecret(clientSecret)
    .WithResource("https://ax.contoso.local")
    .WithOrganizationUrl(
        "https://ax.contoso.local/namespaces/AXSF/")
    .WithGrantType("client_credentials"));
```

ADFS deployments vary. Verify that the authority supports this client-credential/resource form with the environment owner. Version 2.0.0 keeps the existing flow rather than introducing an unverified on-premises protocol change.

The ADFS call uses a named `HttpClient` from `IHttpClientFactory`; it does not create a new unmanaged client for every token acquisition.

## Token Cache

Each named root client owns one token provider and cached token. A cached token is reused until it is within five minutes of expiry.

When many tasks need a token simultaneously:

1. One task enters token acquisition.
2. Other tasks wait on the per-client semaphore.
3. The first successful token is cached.
4. Waiting tasks recheck and reuse it instead of requesting more tokens.

This is single-flight token acquisition and avoids a token-endpoint burst.

## 401 Compare-and-Refresh

If D365 returns 401, an operation asks the shared provider to refresh using the rejected access token as a compare value.

- If another task already replaced that token, the newer cached token is reused.
- Otherwise one task refreshes while peers wait.
- The original D365 operation is rebuilt and sent once with the new token.
- A second 401 is final.

This prevents multiple parallel operations from independently refreshing the same rejected token.

## Named Client Lifetime

```csharp
var factory = provider.GetRequiredService<ID365ClientFactory>();
var sales1 = factory.GetClient("Sales");
var sales2 = factory.GetClient("Sales");
```

Within one service provider, both calls resolve the same lazily created root client. The root client and its factory are safe to use in parallel.

Different names have independent endpoint/options/token state:

```csharp
var finance = factory.GetClient("Finance");
var sales = factory.GetClient("Sales");
```

Different service providers are isolated and may register the same names independently.

## Parallel Query Pattern

Correct:

```csharp
var tasks = ids.Select(id => client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .FirstOrDefaultAsync(cancellationToken));

var rows = await Task.WhenAll(tasks);
```

Incorrect:

```csharp
var sharedQuery = client.Entity<EgrHead>("rvl_egrheads");

var tasks = ids.Select(id => sharedQuery
    .Where(head => head.Id == id)
    .FirstOrDefaultAsync(cancellationToken));
```

`D365Query<T>` stores mutable filters, headers, key identities, paging, and client predicates. It is intentionally not thread-safe. Call `Entity<T>()` once per logical operation.

## Registration Rules

```csharp
services.AddD365ODataClient("Sales", salesConfiguration, "D365:Sales");
services.AddD365ODataClient("Finance", financeConfiguration, "D365:Finance");
```

- Names are case-sensitive (`StringComparison.Ordinal`).
- Registering the same name twice in one service collection throws.
- `GetClient("missing")` throws with a registration error.
- `GetClient()` prefers the name `Default`; otherwise it uses the first registration.
- Injected `ID365Client` resolves `GetClient()`.

For clarity, use a default registration when application code injects `ID365Client` directly. Use explicit names whenever multiple backends exist.

## Cancellation

The caller token reaches MSAL execution, ADFS HTTP calls, token-lock waiting, D365 requests, retry delay, and pagination. Canceling one waiter does not invalidate the shared token or cancel unrelated callers.

## Secret Handling

- Use an external secret provider.
- Never log client secrets or access tokens.
- Rotate any secret exposed in source, logs, chat, or configuration exports.
- Remember that version 2.0.0 accepts all TLS server certificates; authentication secrets are not protected from an active network attacker on an untrusted path.

See [Security and logging](security-and-logging.md).
