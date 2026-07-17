# Migration from Version 1

Version 2.0.0 intentionally removes the version 1 compatibility layer. Upgrade application code and error handling as one change; do not treat this as a package-only version bump.

## API Mapping

| Version 1 | Version 2 |
| --- | --- |
| `ID365Service` | `ID365Client` |
| `ID365ServiceFactory` | `ID365ClientFactory` |
| `factory.GetService(name)` | `factory.GetClient(name)` |
| Non-generic `Entity("name")` entry point | `Entity<T>("name")` |
| Mutation result as string/default | `D365Response` or `D365Response<T>` |
| HTTP failure converted to default result | Typed exception |
| Missing/malformed count converted to zero | `D365ProtocolException` |
| Async calls without cancellation | Optional `CancellationToken` on terminal methods |

## Registration

Before:

```csharp
services.AddD365ODataService(configuration, "D365");
```

After:

```csharp
services.AddD365ODataClient(configuration, "D365");
```

For named clients:

```csharp
services.AddD365ODataClient(
    "Sales",
    configuration,
    "D365:Sales");

var client = factory.GetClient("Sales");
```

Duplicate names in one service collection now fail during registration instead of silently replacing state.

## Query Entry Point

Before:

```csharp
var query = service.Entity("rvl_egrheads");
```

After:

```csharp
var query = client.Entity<EgrHead>("rvl_egrheads");
```

`D365Query<T>` has no public constructor. Obtain it from `ID365Client` so it receives the correct named endpoint, transport, authentication provider, options, and logging policy.

## Read Semantics

Version 1 could swallow a failed GET and return `null`, an empty list, zero, or another default. This made a preflight query unable to distinguish "row does not exist" from 401/500/network failure.

Version 2:

```csharp
var row = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .FirstOrDefaultAsync(cancellationToken);

if (row is null)
{
    // Only a successful, valid empty OData collection reaches here.
}
```

Do not preserve version 1 behavior by catching `D365Exception` and returning `null`. That recreates the duplicate-write risk version 2 is designed to remove.

A collection-level HTTP 404 is an HTTP failure, not an empty collection. Exact missing-row behavior must be represented by a successful collection query with `value: []`.

## Mutation Results

Before:

```csharp
var result = await service.Entity("rvl_egrheads").UpdateAsync(payload);
```

After:

```csharp
D365Response response = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .UpdateAsync(payload, cancellationToken);

Console.WriteLine(response.StatusCode);
Console.WriteLine(response.RequestId);
Console.WriteLine(response.MutationOutcome);
```

High-level mutations throw for every non-2xx status. DELETE 404 is no longer treated as success. An untyped 204 remains a valid success with an empty `RawBody`.

Typed POST returns `D365Response<TResponse>`:

```csharp
var created = await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddHeader("Prefer", "return=representation")
    .AddAsync<EgrHead>(payload, cancellationToken);
```

If the server accepted the create but omitted or malformed the representation, the exception has `MutationOutcome = SucceededOrAccepted`. Reconcile; do not automatically create again.

## Keys

The explicit version 1-compatible identity API remains:

```csharp
await client
    .Entity<EgrHead>("rvl_egrheads")
    .AddIdentity("rvl_egrheadid", id)
    .UpdateAsync(partial, cancellationToken);
```

The preferred model-driven API uses `[OdataKey]` and key-only `Where`:

```csharp
[OdataKey]
[JsonPropertyName("rvl_egrheadid")]
public Guid Id { get; set; }

await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .UpdateAsync(partial, cancellationToken);
```

Annotate all properties for a composite key and include every key in the write expression. Do not model an Edm.Guid key as `string`; strings are quoted by design.

## Count Semantics

Before, an absent or malformed count could become zero.

After:

```csharp
long total = await query.LongCountAsync(cancellationToken);
int smallTotal = await query.CountAsync(cancellationToken);
```

`LongCountAsync` requires a valid, non-negative 64-bit `@odata.count`. `CountAsync` uses checked conversion and throws `OverflowException` if the result cannot fit in an `int`.

## Raw Status Handling

Use raw `SendAsync` only when application code intentionally owns non-2xx handling:

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

Raw responses preserve received HTTP status. High-level query methods use ensured behavior and parse the OData envelope.

## Exception Migration

Handle failures by category rather than parsing log strings:

```csharp
catch (D365AuthenticationException) { }
catch (D365HttpException) { }
catch (D365TransportException) { }
catch (D365ProtocolException) { }
catch (D365SerializationException) { }
catch (D365OperationCanceledException) { }
```

`D365OperationCanceledException` derives from `OperationCanceledException`, not `D365Exception`, so existing standard cancellation handlers still work.

## Cancellation and Retry

Pass the application request token to every terminal operation:

```csharp
await query.ToListAsync(httpContext.RequestAborted);
```

Read retry is disabled by default. If enabled, only GET/HEAD are retried for the documented transient conditions. Mutations are not automatically retried after timeout, transport failure, 408, 429, or 5xx because their outcome may be unknown.

## Upgrade Checklist

1. Replace the version 1 service and factory interfaces.
2. Change all query entry points to `Entity<T>`.
3. Update mutation code to consume `D365Response`.
4. Remove catch-and-return-default adapters.
5. Distinguish successful empty reads from exceptions.
6. Add `CancellationToken` propagation.
7. Review every POST/PATCH/DELETE retry for exact-key reconciliation.
8. Verify all Dataverse GUID keys use `Guid`.
9. Run unit tests for 401, 404, 429, 500, timeout, malformed JSON, and second-page failure.
10. Review the version 2 TLS warning before production rollout.
