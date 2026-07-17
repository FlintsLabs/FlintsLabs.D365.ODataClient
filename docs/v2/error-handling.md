# Error Handling

Version 2 uses a fail-closed contract for high-level operations. A result represents a successful and validated D365 response; failures remain exceptions and are not converted into business values.

## Successful Empty vs Failure

```csharp
var row = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .FirstOrDefaultAsync(cancellationToken);

if (row is null)
{
    // D365 returned 2xx with a valid collection and value: [].
}
```

The following cases do not return `null`:

- HTTP 401, 403, 404, 408, 429, or 5xx.
- DNS, socket, TLS, connection, and response-stream failures.
- Request timeout or caller cancellation.
- Invalid JSON.
- A 2xx OData response with missing/non-array `value`.
- A 2xx OData error envelope.
- Entity deserialization failure.

`ToListAsync` follows the same rule: an empty list is successful business data, not an error fallback.

## Exception Hierarchy

```text
Exception
|- D365Exception
|  |- D365HttpException
|  |  `- D365AuthenticationException
|  |- D365TransportException
|  |- D365ProtocolException
|  `- D365SerializationException
`- OperationCanceledException
   `- D365OperationCanceledException
```

`D365OperationCanceledException` deliberately remains an `OperationCanceledException`. Catch it before a broad cancellation handler only when mutation outcome is needed.

Authentication authority libraries can also throw their native exceptions while obtaining a token before a D365 request is sent. These still fail closed; do not convert them into an empty read.

## Failure Kinds

| `D365FailureKind` | Meaning |
| --- | --- |
| `Http` | D365 returned a non-success HTTP response |
| `Authentication` | Final D365 HTTP response is 401 after one refresh attempt |
| `Transport` | No HTTP response was available because transport/read failed |
| `Timeout` | The package request timeout elapsed |
| `Serialization` | JSON was invalid or could not be converted to the requested model |
| `Protocol` | A response violated the expected OData/package contract |

## D365Exception Properties

| Property | Meaning |
| --- | --- |
| `FailureKind` | Stable failure category |
| `StatusCode` | HTTP status when a response exists |
| `Method` | HTTP method when known |
| `RequestUri` | Request URI when known; it may contain sensitive filter values |
| `EntityName` | Entity set associated with the operation |
| `ResponseBody` | Buffered error/success body attached to the failure |
| `D365ErrorCode` | Parsed `error.code`, when available |
| `D365ErrorMessage` | Parsed `error.message`, when available |
| `RequestId` | D365/Azure request or correlation header, when available |
| `IsTransient` | Whether the underlying condition may be temporary |
| `MutationOutcome` | Whether a write was sent/accepted/rejected/unknown |
| `RetryAfter` | Parsed HTTP `Retry-After`, when available |
| `PartialRecordCount` | Records accumulated before a paged query failed |

High-level HTTP exception bodies are capped at 64 KiB by default. The body and URI can contain business or personal data; do not log them by default.

## HTTP Errors

```csharp
catch (D365HttpException exception)
{
    logger.LogWarning(
        "D365 HTTP {Status}; entity={Entity}; code={Code}; request={RequestId}; transient={Transient}",
        exception.StatusCode,
        exception.EntityName,
        exception.D365ErrorCode,
        exception.RequestId,
        exception.IsTransient);
}
```

`D365HttpException` covers non-2xx high-level responses. The client parses common D365 JSON error envelopes into `D365ErrorCode` and `D365ErrorMessage`.

The following statuses are marked transient: 408, 429, 500, 502, 503, and 504. `IsTransient` does not mean a mutation is safe to retry.

## Authentication Errors

The transport performs one compare-and-refresh cycle only after receiving a real HTTP 401. If the second D365 response is still 401:

- High-level operations throw `D365AuthenticationException`.
- Raw `ID365Client.SendAsync` returns the final 401 response.

There is no refresh loop. A timeout or missing HTTP response is not treated as evidence that authentication was rejected.

## Transport and Timeout

```csharp
catch (D365TransportException exception)
{
    if (exception.FailureKind == D365FailureKind.Timeout)
    {
        // The per-request timeout elapsed.
    }
}
```

A transport/timeout read has `MutationOutcome = NotApplicable`. A mutation may have `NotSent` or `Unknown` depending on whether sending began.

No HTTP status or response body exists for a transport failure. Preserve `InnerException` for diagnostics without exposing it to untrusted clients.

## Protocol Errors

Examples:

- Collection response is not a JSON object.
- `value` is missing or is not an array.
- A 2xx response contains an OData `error` envelope.
- `@odata.count` is missing, negative, non-numeric, or outside `Int64`.
- `@odata.nextLink` is malformed, cross-origin, outside the configured API base path, or loops.
- Pagination exceeds `MaxPages`.
- A typed successful mutation has an empty/null body.

Protocol failures are not business-level "not found" results.

## Serialization Errors

A malformed JSON document or entity conversion failure throws `D365SerializationException`. For a typed mutation, serialization failure can occur after the server has accepted the write; inspect `MutationOutcome` before recovery.

## Cancellation

```csharp
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.NotSent)
{
    // Caller canceled before the mutation started sending.
}
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    // Sending began. Reconcile exact state before any retry.
}
```

Cancellation is not proof that D365 canceled or rolled back a write.

## Pagination Failure

`ToListAsync` and client-filtered counts do not return partial data. If page N fails, the exception includes the number of records accepted from earlier pages:

```csharp
catch (D365Exception exception)
{
    logger.LogWarning(
        "Paged D365 query failed after {Count} records; request={RequestId}",
        exception.PartialRecordCount,
        exception.RequestId);
    throw;
}
```

Use this count for diagnostics only. Do not process it as a complete result.

## Recommended Boundary

At a repository or gateway boundary:

1. Return `null`/not-found only for a successful empty exact query.
2. Let infrastructure exceptions cross to a centralized error policy or map them to an explicit unavailable result.
3. Preserve HTTP/D365 request IDs for support.
4. Never catch all exceptions and return an empty collection.
5. Use exact-key reconciliation for any mutation with outcome `Unknown` or `SucceededOrAccepted` plus response parsing failure.
