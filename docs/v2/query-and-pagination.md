# Queries and Pagination

`D365Query<T>` is a mutable fluent builder created by `ID365Client.Entity<T>()`. It translates supported expression trees to OData and keeps literal formatting separate from URI encoding.

## Build a Query

```csharp
var rows = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head =>
        head.WmsStatus == false &&
        head.Name!.StartsWith(prefix))
    .Select(head => new { head.Id, head.Name, head.WmsStatus })
    .OrderBy(head => head.Name)
    .ThenByDescending(head => head.Id)
    .PageSize(250)
    .Take(500)
    .ToListAsync(cancellationToken);
```

Query options are combined deterministically. Supported fluent operations include:

- `Where`
- `Select`
- `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`
- `Expand`
- `Skip`, `Take`
- `Count`, `CountAsync`, `LongCountAsync`
- `CrossCompany`
- `PageSize`, `AddHeader`
- Explicit `WhereClient`

Calling `Where` more than once combines server filters with `and`.

## Property Names

`[JsonPropertyName]` controls the OData field name:

```csharp
[JsonPropertyName("rvl_wmsstatus")]
public bool? WmsStatus { get; set; }
```

The translator emits `rvl_wmsstatus`, not `WmsStatus`, in filters, selects, ordering, and key expressions.

## Supported Filter Operations

Binary comparisons and logic:

```csharp
.Where(x =>
    x.Quantity >= minimum &&
    (x.Status == "Open" || x.Status == "Backorder"))
```

Supported operators are `==`, `!=`, `>`, `>=`, `<`, `<=`, `&&`, and `||`.

String functions:

```csharp
.Where(x =>
    x.Name!.Contains("A&B") ||
    x.Name.StartsWith("SO") ||
    x.Name.EndsWith("-TH"))
```

Collection membership is expanded to equality joined by `or`:

```csharp
var statuses = new[] { "Open", "Backorder" };
var query = client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order => statuses.Contains(order.Status!));
```

An empty collection becomes `false`.

Nullable boolean `GetValueOrDefault()` is supported, including unary `!`.

## Null Coalescing

Simple C# null coalescing translates to OData `coalesce(left,right)`:

```csharp
var query = client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => (head.Name ?? "") == expectedName);
```

Logical filter before URI encoding:

```text
coalesce(rvl_name,'') eq 'expected value'
```

Coalesce expressions with a conversion lambda are not supported. Unsupported expression translation fails immediately with a clear `NotSupportedException` that includes the expression; it does not crash later or silently fall back to client evaluation.

## Literal Formatting

| CLR value | OData form |
| --- | --- |
| `null` | `null` |
| `string` / `char` | Single-quoted; embedded apostrophes doubled |
| `Guid` | Unquoted OData v4 GUID |
| Integer/floating/decimal | Invariant culture |
| `DateTime` | UTC round-trip value |
| `DateTimeOffset` | Converted to UTC |
| `DateOnly` | `yyyy-MM-dd` |
| `bool` | Configured `NoYesEnum` or OData `true`/`false` |
| CLR enum | Quoted enum member name |

Dataverse normally needs:

```csharp
.WithBooleanFormatting(D365BooleanFormatting.Literal)
```

F&O defaults to the package NoYes enum literal.

If an entity key is Edm.Guid, declare it as `Guid`. A `string` that looks like a GUID remains a string and is quoted.

## URI Encoding

The query builder URI-encodes OData option values after translation. This protects reserved values including spaces, `#`, `&`, `+`, `%`, apostrophes, and Unicode from being interpreted as query separators or fragments.

Do not pre-encode captured values. Pass normal CLR values and let the literal/URI layers handle them.

## Keys for Update and Delete

Single key:

```csharp
public sealed class EgrHead
{
    [OdataKey]
    [JsonPropertyName("rvl_egrheadid")]
    public Guid Id { get; set; }
}

await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == headId)
    .DeleteAsync(cancellationToken);
```

Composite key:

```csharp
public sealed class SalesOrder
{
    [OdataKey]
    [JsonPropertyName("SalesOrderNumber")]
    public string Number { get; set; } = string.Empty;

    [OdataKey]
    [JsonPropertyName("dataAreaId")]
    public string Company { get; set; } = string.Empty;
}

await client
    .Entity<SalesOrder>("SalesOrderHeadersV2")
    .Where(order =>
        order.Number == number &&
        order.Company == company)
    .UpdateAsync(partial, cancellationToken);
```

Write validation rules:

- The model must contain at least one `[OdataKey]`, unless `AddIdentity`/anonymous keys are used.
- Every annotated key must be supplied.
- The write `Where` may contain key equality only.
- Non-key filters are rejected before HTTP is sent.
- The `$filter` is removed when converted to an entity-key URL.

Fallback:

```csharp
.AddIdentity("rvl_egrheadid", headId)
```

## Count

```csharp
long total = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.WmsStatus == false)
    .LongCountAsync(cancellationToken);
```

Server count sends `$count=true&$top=0` and requires a valid non-negative 64-bit `@odata.count`.

`CountAsync` calls the same strict path and uses checked conversion to `int`. Missing/malformed count throws `D365ProtocolException`; overflow throws `OverflowException`.

With `WhereClient`, counting scans pages and counts client matches instead of relying on `@odata.count`.

## WhereClient

`WhereClient` is explicit client-side evaluation over each JSON record:

```csharp
var matches = await client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Company == company)
    .WhereClient(head => head.Name!.Contains(localMarker))
    .Take(20)
    .ToListAsync(cancellationToken);
```

Operational implications:

- Server `Where` should narrow the dataset first.
- Multiple server pages may be downloaded.
- `Take(20)` applies to matched client records, not the first 20 server records.
- The query may be expensive and is not a silent fallback for unsupported LINQ.

## Pagination

The client follows both relative and absolute `@odata.nextLink` values. Before sending the bearer token, every link is validated:

- Scheme must be HTTP/HTTPS and match the configured base.
- Host and port must match.
- Path must remain under the configured OData API base path.
- User information and fragments are rejected.
- Normalized repeated links are rejected as loops.
- Fetching beyond `MaxPages` is rejected.

`MaxPages` defaults to 10,000.

## All-or-Error Results

If any page returns non-2xx, times out, is canceled, contains invalid JSON, or has an invalid next link, `ToListAsync` throws and does not return the records from earlier pages.

For failures represented by `D365Exception`, `PartialRecordCount` reports records already collected. It is diagnostic evidence only, not a partial success result.

## Concurrency

A query builder is mutable and not thread-safe. This is safe:

```csharp
await Task.WhenAll(ids.Select(id => client
    .Entity<EgrHead>("rvl_egrheads")
    .Where(head => head.Id == id)
    .FirstOrDefaultAsync(cancellationToken)));
```

Do not mutate and execute one shared `D365Query<T>` from multiple tasks.
