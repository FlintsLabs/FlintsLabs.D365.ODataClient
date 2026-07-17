# Mutations and Responses

Version 2 separates high-level ensured operations from raw status handling. Choose the API based on who owns HTTP status interpretation.

## High-Level Mutation API

```csharp
Task<D365Response> AddAsync(
    T entity,
    CancellationToken cancellationToken = default);

Task<D365Response> AddAsync(
    object payload,
    CancellationToken cancellationToken = default);

Task<D365Response<TResponse>> AddAsync<TResponse>(
    object payload,
    CancellationToken cancellationToken = default);

Task<D365Response> UpdateAsync(
    T entity,
    CancellationToken cancellationToken = default);

Task<D365Response> UpdateAsync(
    object partialPayload,
    CancellationToken cancellationToken = default);

Task<D365Response> UpdateAsync(
    object keys,
    T entity,
    CancellationToken cancellationToken = default);

Task<D365Response> DeleteAsync(
    CancellationToken cancellationToken = default);
```

These methods return only for HTTP 2xx. Every non-2xx, including DELETE 404, throws.

## D365Response

```csharp
public sealed record D365Response(
    HttpStatusCode StatusCode,
    string RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    Uri RequestUri,
    string? RequestId,
    D365MutationOutcome MutationOutcome);
```

The generic response adds `T? Value`. Both response types expose computed `IsSuccessStatusCode` and `EnsureSuccessStatusCode()`.

`RawBody`, headers, and request URI are buffered diagnostics. They may contain sensitive values and are not safe for unrestricted logs.

## Create

Use an untyped result when D365 commonly returns 201/204 without a representation:

```csharp
D365Response response = await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddAsync(payload, cancellationToken);
```

Use a typed result only when the endpoint returns JSON. Dataverse commonly supports `Prefer: return=representation`:

```csharp
D365Response<EgrHead> response = await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddHeader("Prefer", "return=representation")
    .AddAsync<EgrHead>(payload, cancellationToken);
```

Typed success requirements:

- Body must not be empty or whitespace.
- Body must be valid JSON for `TResponse`.
- Deserialized value must not be null.

If these checks fail after 2xx, the thrown protocol/serialization exception has `MutationOutcome = SucceededOrAccepted`. The create may already exist.

## Update

Preferred key-based update:

```csharp
await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == headId)
    .UpdateAsync(
        new { rvl_wmsstatus = false },
        cancellationToken);
```

Compatibility identity update:

```csharp
await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddIdentity("rvl_egrheadid", headId)
    .UpdateAsync(
        new { rvl_wmsstatus = false },
        cancellationToken);
```

Typed entity with anonymous keys:

```csharp
await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .UpdateAsync(
        new { SalesOrderNumber = number, dataAreaId = company },
        updatedEntity,
        cancellationToken);
```

For `[OdataKey]` writes, `Where` must contain equality on all and only key properties. General filter-based mass update/delete is not supported.

## Delete

```csharp
await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == headId)
    .DeleteAsync(cancellationToken);
```

DELETE 204 is success. DELETE 404 throws `D365HttpException` with `MutationOutcome = Rejected`; idempotent delete behavior must be explicitly implemented by the caller if desired.

## Mutation Outcome

| Condition | Outcome | Caller action |
| --- | --- | --- |
| Non-mutation read | `NotApplicable` | Use read exception/status semantics |
| Caller cancellation before send starts | `NotSent` | A retry may be considered after validating intent |
| Received 2xx | `SucceededOrAccepted` | Treat the mutation as accepted; reconcile if response parsing fails |
| Received non-2xx other than 408/5xx | `Rejected` | Inspect status/error; mutation was rejected by this response |
| Received 408 or 5xx | `Unknown` | Query exact state before retry |
| Timeout/transport/cancellation after send starts | `Unknown` | Query exact state before retry |

The package classifies any received non-2xx below 500 other than 408 as `Rejected`, including 400, 401, 403, 404, 409, 412, 422, and 429. HTTP 429 is transient for read retry decisions but rejected as a received mutation response.

`SucceededOrAccepted` means D365 returned a success status. It does not guarantee that every asynchronous downstream business process has completed.

## Ambiguous Mutation Pattern

```csharp
try
{
    await client
        .Entity<EgrHead>("rvl_egrheads")
        .AddAsync(payload, cancellationToken);
}
catch (D365Exception exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    var existing = await client
        .Entity<EgrHead>("rvl_egrheads")
        .Where(head => head.RunningNumber == runningNumber)
        .FirstOrDefaultAsync(cancellationToken);

    if (existing is null)
    {
        // Only now may caller-controlled retry policy consider another POST.
    }
}
```

Use a stable unique correlation key such as external ID or running number. Do not reconcile by ordering by creation date or taking the latest row unless the business system proves that correlation is exact.

## Raw SendAsync

```csharp
D365Response response = await client.SendAsync(
    HttpMethod.Patch,
    $"rvl_egrheads({headId})",
    new { rvl_wmsstatus = false },
    cancellationToken);
```

Raw behavior:

- Returns every received HTTP response, including 4xx and 5xx.
- Performs the same token acquisition and one actual-401 refresh.
- Throws when no response exists because of transport, timeout, or cancellation.
- Does not parse a collection or typed entity body.
- Does not automatically call `EnsureSuccessStatusCode()`.

A raw caller must branch on `StatusCode` or call `response.EnsureSuccessStatusCode()`.

## Headers

`AddHeader` applies caller-provided headers to the query operation:

```csharp
var query = client
    .Entity<EgrHead>("rvl_egrheads")
    .AddHeader("Prefer", "return=representation");
```

Do not add `Authorization`; the transport owns bearer authentication. Avoid logging custom header values because they may contain sensitive data.

## No Automatic Mutation Retry

POST, PATCH, and DELETE are never automatically retried after timeout, transport failure, 408, 429, or 5xx. A mutation can have committed even when the response was lost. Retry is application-controlled only after exact reconciliation.

An actual HTTP 401 is the exception: the rejected request is resent once with a refreshed token because the server explicitly rejected authentication.
