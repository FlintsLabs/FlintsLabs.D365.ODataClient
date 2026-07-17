# BioWMS Recovery Pattern

This pattern prevents a failed D365 preflight from being misclassified as "not found" and causing a duplicate POST. It is useful for gateway/import/recovery flows that synchronize BioWMS records with Dataverse or F&O.

## Required Invariant

Only this means "the exact row does not exist":

```text
HTTP 2xx + valid OData collection + value: []
```

The following mean "preflight failed" and must stop the create path:

- 401/403 authentication or authorization error.
- Collection-level 404.
- 408/429/5xx.
- Timeout, DNS, TLS, socket, or response-stream failure.
- Invalid JSON.
- Missing/non-array `value`.
- Pagination failure.
- Caller cancellation.

Version 2 preserves this distinction by throwing instead of returning `null` for failures.

## Stable Correlation Key

Every create workflow should have an exact stable key known before POST, for example:

- `RunningNumber`.
- External message/event ID.
- Source-system document ID plus company.
- A Dataverse alternate key.

Do not use "latest row", timestamp proximity, or sort order as proof that a specific request succeeded.

Example model:

```csharp
public sealed class EgrHead
{
    [OdataKey]
    [JsonPropertyName("rvl_egrheadid")]
    public Guid Id { get; set; }

    [JsonPropertyName("rvl_runningnumber")]
    public string RunningNumber { get; set; } = string.Empty;

    [JsonPropertyName("rvl_wmsstatus")]
    public bool? WmsStatus { get; set; }
}
```

## Exact Preflight

```csharp
var existing = await dataverse
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.RunningNumber == runningNumber)
    .FirstOrDefaultAsync(cancellationToken);

if (existing is not null)
{
    return Existing(existing.Id);
}

// The create branch is reachable only after a successful empty query.
```

Do not wrap this in a catch that returns `null`:

```csharp
// Unsafe: recreates version 1's swallowed-error behavior.
catch (D365Exception)
{
    return null;
}
```

## Safe Create Flow

```csharp
try
{
    var created = await dataverse
        .Entity<EgrHead>("rvl_egrheads")
        .AddHeader("Prefer", "return=representation")
        .AddAsync<EgrHead>(
            new
            {
                rvl_runningnumber = runningNumber,
                rvl_wmsstatus = false
            },
            cancellationToken);

    return Created(created.Value!.Id);
}
catch (D365Exception exception)
    when (exception.MutationOutcome is
        D365MutationOutcome.Unknown or
        D365MutationOutcome.SucceededOrAccepted)
{
    return await ReconcileAsync(
        dataverse,
        runningNumber,
        cancellationToken);
}
catch (D365OperationCanceledException exception)
    when (exception.MutationOutcome == D365MutationOutcome.Unknown)
{
    return await ReconcileAsync(
        dataverse,
        runningNumber,
        CancellationToken.None);
}
```

Choose the reconciliation token deliberately. If the original caller token is canceled, a bounded independent background/recovery workflow may be needed; do not let it run indefinitely.

## Exact Reconciliation

```csharp
static async Task<RecoveryResult> ReconcileAsync(
    ID365Client dataverse,
    string runningNumber,
    CancellationToken cancellationToken)
{
    var row = await dataverse
        .Entity<EgrHead>("rvl_egrheads")
        .Where(head => head.RunningNumber == runningNumber)
        .FirstOrDefaultAsync(cancellationToken);

    if (row is not null)
    {
        return RecoveryResult.AlreadyApplied(row.Id);
    }

    return RecoveryResult.ProvenAbsent();
}
```

If reconciliation GET fails, its exception must remain a failure. Do not reinterpret it as absence and do not POST again.

`ProvenAbsent` does not itself perform the retry. It returns control to an application policy that can check attempt count, idempotency constraints, queue state, and operator approval.

## Outcome Decisions

| Outcome | Meaning | BioWMS action |
| --- | --- | --- |
| `NotSent` | Mutation sending did not begin | Caller may reschedule after validating intent |
| `Rejected` | A final HTTP response rejected the mutation | Handle status/business error; do not reconcile as ambiguous transport |
| `SucceededOrAccepted` | D365 returned 2xx | Treat as accepted; reconcile if typed body parsing failed |
| `Unknown` | Write may or may not have committed | Exact query before any retry |

For `SucceededOrAccepted` plus typed response failure, the safest assumption is that the record may already exist.

## Update Recovery

For PATCH timeout/transport/5xx:

1. Query the row by exact primary/alternate key.
2. Read the fields or version marker relevant to the intended update.
3. If state already matches, mark the operation complete.
4. If state differs, apply an application-owned retry/concurrency policy.
5. If the read fails, stop and surface unavailable.

Do not assume PATCH was rolled back because the local request timed out.

## Delete Recovery

For DELETE with unknown outcome:

1. Query by exact key.
2. Successful empty means deletion is now observed.
3. Successful existing row means deletion was not observed.
4. Failed GET remains unavailable.

A direct high-level DELETE 404 is `Rejected` and throws. If business semantics consider already-absent success, convert that specific status only at the application boundary and keep all other errors intact.

## UI and Operational State

If BioWMS has previously loaded data and D365 becomes unavailable:

- Keep stale data visible when appropriate.
- Mark it stale with last-success time.
- Show connectivity/unavailable state separately from no-record state.
- Do not replace prior data with an empty collection.
- Preserve D365 request IDs for support without exposing response bodies.

## Suggested State Machine

```text
Preflight exact GET
|- Success + row -> Existing
|- Success + empty -> POST
`- Failure -> Unavailable (no POST)

POST
|- 2xx -> Created/Accepted
|- Rejected -> Failed
|- NotSent -> Reschedule by policy
`- Unknown -> Exact reconciliation GET
   |- Success + row -> Applied
   |- Success + empty -> Proven absent; caller decides retry
   `- Failure -> Unavailable; no retry
```

## Tests to Keep

1. Successful empty preflight permits one POST.
2. Preflight 401/500/timeout/malformed JSON permits zero POSTs.
3. POST timeout followed by exact-row success produces no second POST.
4. POST timeout followed by exact empty result enters caller policy, not automatic retry.
5. Reconciliation GET failure permits zero additional POSTs.
6. Concurrent processing uses the same stable correlation key and backend uniqueness constraint where possible.
7. Stale UI state remains distinguishable from successful empty state.
