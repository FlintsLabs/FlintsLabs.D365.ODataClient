# Retry, Timeout, and Cancellation

The version 2 policy is conservative: automatic retry is disabled by default and never retries an ambiguous mutation.

## Enable Read Retry

```csharp
services.AddD365ODataClient("Sales", d365 => d365
    .UseAzureAD()
    .WithOrganizationUrl(organizationUrl)
    .WithResource(resource)
    .WithTenantId(tenantId)
    .WithClientId(clientId)
    .WithClientSecret(clientSecret)
    .ConfigureRetry(retry =>
    {
        retry.MaxReadRetries = 2;
        retry.BaseDelay = TimeSpan.FromMilliseconds(250);
        retry.MaxDelay = TimeSpan.FromSeconds(10);
        retry.UseJitter = true;
    }));
```

Defaults:

| Option | Default |
| --- | --- |
| `MaxReadRetries` | `0` |
| `BaseDelay` | 250 ms |
| `MaxDelay` | 30 seconds |
| `UseJitter` | `true` |

`MaxReadRetries` is the number of retries after the initial request. A value of 2 allows at most three attempts.

Invalid options fail during registration: retries cannot be negative, delays must be positive, and base delay cannot exceed max delay.

## Configuration Keys

When using `FromConfiguration`, retry keys are:

```json
{
  "D365": {
    "Retry": {
      "MaxReadRetries": 2,
      "BaseDelay": "00:00:00.250",
      "MaxDelay": "00:00:10",
      "UseJitter": true
    }
  }
}
```

## Retry Matrix

| Condition | GET/HEAD when enabled | POST/PATCH/DELETE |
| --- | --- | --- |
| HTTP 408 | Retry | No retry |
| HTTP 429 | Retry | No retry |
| HTTP 500 | Retry | No retry |
| HTTP 502 | Retry | No retry |
| HTTP 503 | Retry | No retry |
| HTTP 504 | Retry | No retry |
| Transient transport/response read failure | Retry | No retry |
| Per-request timeout | Retry | No retry |
| Actual HTTP 401 | One token refresh/resend | One token refresh/resend |
| Caller cancellation | Never | Never |

The one-401 authentication refresh is independent of `MaxReadRetries`. It occurs only after a real 401 response and at most once per operation.

## Backoff and Retry-After

For retryable read responses, the client:

1. Uses `Retry-After` delta seconds or HTTP date when valid.
2. Caps that delay at `MaxDelay`.
3. Otherwise applies bounded exponential backoff from `BaseDelay`.
4. Applies jitter by default.

If the final high-level HTTP response still fails, `D365HttpException.RetryAfter` preserves the parsed header value. This property describes server guidance; it does not grant permission to retry a mutation.

## Request Timeout

The named `HttpClient` timeout is infinite. The package applies `D365ClientOptions.RequestTimeout` per operation so it can distinguish package timeout from caller cancellation. The default is 100 seconds.

Advanced named options can be set after client registration:

```csharp
services.AddD365ODataClient("Sales", configuration, "D365:Sales");
services.Configure<D365ClientOptions>("Sales", options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.MaxPages = 500;
    options.MaxErrorBodyBytes = 32 * 1024;
});
```

Register this override after `AddD365ODataClient` so it runs after the package snapshot configuration. `Timeout.InfiniteTimeSpan` disables the package timeout; any other value must be positive.

A package timeout throws `D365TransportException` with:

- `FailureKind = Timeout`.
- `IsTransient = true`.
- `MutationOutcome = NotApplicable` for reads.
- `MutationOutcome = Unknown` for a mutation after sending begins.

## Caller Cancellation

Every terminal operation accepts `CancellationToken`:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

var rows = await client
    .Entity<EgrHead>("rvl_egrheads")
    .ToListAsync(timeout.Token);
```

Caller cancellation throws `D365OperationCanceledException`, which derives from `OperationCanceledException` and carries `MutationOutcome`.

Cancellation propagates through:

- Token cache lock waiting.
- Azure AD and Managed Identity token acquisition.
- ADFS HTTP token request.
- Request sending and response-body reading.
- Retry delay.
- Every pagination page.

## Mutation Cancellation

```csharp
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.NotSent)
{
    // Sending had not begun.
}
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    // Sending began; inspect D365 using an exact correlation key.
}
```

A caller token stopping local work does not prove the remote operation was canceled.

## Why Mutations Are Not Retried

For POST/PATCH/DELETE, the server may commit and the response may be lost. Repeating automatically can create duplicates or apply a change twice. Therefore:

1. Include a stable business/correlation key in the mutation.
2. On `Unknown`, issue an exact fail-closed GET by that key.
3. If the GET fails, stop; do not interpret failure as absence.
4. Retry only if a successful exact query proves the mutation is absent and the application policy allows it.

## IsTransient Is Not IsRetrySafe

`IsTransient` classifies the underlying infrastructure condition. It is useful for alerts and read policies. It does not encode idempotency, mutation outcome, or business safety.

Use both operation type and `MutationOutcome`; never write `if (exception.IsTransient) retry` for a mutation.
