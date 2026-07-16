# FlintsLabs D365 OData Client 2.0 Design

**Status:** Approved design, awaiting implementation plan  
**Target release:** `2.0.0`  
**Package:** `FlintsLabs.D365.ODataClient`

## Summary

Version 2.0 makes the client fail closed: a failed D365 request must never be
reported as `null`, an empty list, `0`, an empty string, or `default(T)`. The
public API keeps simple LINQ-like query methods for normal callers and adds a
raw response API for workflows that need status codes, headers, and mutation
outcome details.

This is an intentional breaking release. It removes the non-generic legacy
`D365Service` API instead of preserving unsafe compatibility behavior.

## Goals

- Distinguish successful empty results from HTTP, transport, timeout, protocol,
  serialization, and cancellation failures.
- Centralize all HTTP execution and error handling in one internal transport.
- Preserve status, headers, D365 error details, request IDs, retry metadata, and
  mutation outcome information.
- Make token refresh safe for concurrent callers and bound 401 retries to one.
- Prevent silent partial results during pagination.
- Make query construction safe for OData literals and URL-reserved characters.
- Keep the normal query API concise while exposing a low-level raw API.
- Provide complete v1-to-v2 migration and operational documentation after the
  production implementation is complete.

## Non-Goals

- Preserve binary or behavioral compatibility with version 1.x.
- Keep the non-generic `D365Service` or `ID365Service` API.
- Add automatic mutation retries for timeout, network, 408, 429, or 5xx errors.
- Add a key lookup API that maps HTTP 404 to `null` in the first 2.0 release.
- Change the current TLS certificate-validation behavior in this release.

## Accepted TLS Risk

The current client accepts untrusted certificates for D365 and ADFS HTTP
clients. Version 2.0 intentionally leaves that behavior unchanged by product
decision. The official security documentation must state that this weakens TLS
authentication and can expose tokens or business data to a man-in-the-middle
attacker. The release must not claim secure-by-default certificate handling.

## Current Failure Modes

Version 1.2.27 has several ambiguous result paths:

- `D365Query<T>` converts non-success GET responses and transport exceptions to
  a null JSON document.
- `ToListAsync()` stops pagination and returns records already collected when a
  later page fails.
- `FirstOrDefaultAsync()` converts that empty or partial list into `null`.
- `CountAsync()` returns `0` on request failure and `-1` when count parsing fails.
- Typed POST returns `default(T)` on non-success or deserialization failure.
- POST, PATCH, and DELETE string methods return error bodies through the same
  return channel as successful responses.
- The 401 log says that a retry will occur, but the generic query path returns a
  null result instead.
- Legacy methods recursively retry 401 without invalidating the shared token.
- Mutation methods do not accept cancellation tokens.
- Pagination accepts next links without host, base-path, or loop validation.
- Client registrations use global static state and leak between service
  providers.

## Design Principles

1. `null`, an empty list, and `0` represent only successful empty results.
2. High-level APIs throw typed exceptions on every non-success condition.
3. Raw APIs expose HTTP non-success responses without misclassifying them as
   successful domain values.
4. No mutation is automatically retried after an ambiguous outcome.
5. Every asynchronous operation accepts and propagates a cancellation token.
6. A failed page invalidates the whole paginated query result.
7. Root clients are safe for concurrent use; query builders are not shared.
8. Error and retry decisions are implemented once in the transport layer.

## Architecture

### `D365Client`

`D365Client` is the root public client. It creates typed entity query builders
and exposes the raw HTTP API. It is stateless and safe for concurrent use.

```csharp
public interface ID365Client
{
    D365Query<T> Entity<T>(string entity);
    D365Query<T> Entity<T>(Enum entity);

    Task<D365Response> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? payload = null,
        CancellationToken cancellationToken = default);
}
```

`ID365Service`, `D365Service`, `ID365ServiceFactory`, and
`D365ServiceFactory` are replaced by `ID365Client`, `D365Client`,
`ID365ClientFactory`, and `D365ClientFactory`.

### `D365Query<T>`

`D365Query<T>` remains the stateful fluent builder for filters, selection,
ordering, expansion, pagination, and entity mutations. Every call to
`Entity<T>()` returns a new builder. A query builder must not be shared between
parallel tasks.

The builder no longer creates or sends `HttpRequestMessage` instances. It
builds a request description and delegates execution to `ID365Transport`.

### `ID365Transport`

`ID365Transport` is internal and is the only component allowed to send D365
HTTP requests. It owns:

- bearer-token attachment;
- per-request timeout handling;
- one bounded 401 refresh attempt;
- optional read-only retries;
- response buffering and disposal;
- response header and request-ID capture;
- D365 error-envelope parsing;
- exception construction;
- mutation-outcome classification;
- sanitized request and response logging.

It provides separate raw and ensured execution paths so that high-level APIs
cannot accidentally use raw non-success responses as domain values.

### Query Construction Components

`ODataQueryBuilder` stores query options as structured values and creates the
relative request URI. `ODataLiteralFormatter` formats values independently of
URI encoding. LINQ translation continues to produce OData expressions, but it
does not concatenate unencoded values directly into request URIs.

## Public Query API

```csharp
public Task<T?> FirstOrDefaultAsync(
    CancellationToken cancellationToken = default);

public Task<List<T>> ToListAsync(
    CancellationToken cancellationToken = default);

public Task<int> CountAsync(
    CancellationToken cancellationToken = default);

public Task<long> LongCountAsync(
    CancellationToken cancellationToken = default);
```

### Read Semantics

- `FirstOrDefaultAsync()` returns `null` only after a successful 2xx response
  containing a valid OData collection with an empty `value` array.
- `ToListAsync()` returns an empty list only for a successful query whose valid
  collection contains no records.
- HTTP 404 from a collection query is an error, not a missing record.
- No first-release method maps key-lookup 404 responses to `null`.
- A collection response must be a JSON object containing `value` as an array.
- Missing `value`, the wrong `value` type, or an OData error envelope in a 2xx
  response raises `D365ProtocolException`.
- Malformed JSON or record deserialization failure raises
  `D365SerializationException`.
- `CountAsync()` parses the OData count as `long` and throws `OverflowException`
  if the value cannot fit in `int`.
- `LongCountAsync()` returns the full parsed `long` count.
- Missing or invalid `@odata.count` is a protocol failure when a count was
  requested.

## Public Mutation API

```csharp
public Task<D365Response> AddAsync(
    T entity,
    CancellationToken cancellationToken = default);

public Task<D365Response> AddAsync(
    object payload,
    CancellationToken cancellationToken = default);

public Task<D365Response<TResponse>> AddAsync<TResponse>(
    object payload,
    CancellationToken cancellationToken = default);

public Task<D365Response> UpdateAsync(
    T entity,
    CancellationToken cancellationToken = default);

public Task<D365Response> UpdateAsync(
    object partialPayload,
    CancellationToken cancellationToken = default);

public Task<D365Response> DeleteAsync(
    CancellationToken cancellationToken = default);
```

Existing key selection through `AddIdentity()`, anonymous key objects, and
`Where()` over complete `[OdataKey]` equality predicates remains supported.

### Mutation Semantics

- High-level mutation methods return only successful 2xx responses.
- Non-2xx responses raise a typed D365 exception with status and error details.
- HTTP 200, 201, 202, and 204 are successful responses.
- Empty bodies are valid for untyped mutation methods.
- A typed mutation method requires a non-empty body that deserializes to the
  requested response type.
- A typed method receiving a successful response with an empty body raises
  `D365ProtocolException` with outcome `SucceededOrAccepted`.
- A typed method receiving malformed success JSON raises
  `D365SerializationException` with outcome `SucceededOrAccepted`.
- DELETE 404 raises by default. Idempotent delete behavior is not implicit.
- Error JSON is never returned through a success string channel.

## Raw Response API

`ID365Client.SendAsync()` returns a buffered `D365Response` for every received
HTTP response, including non-2xx responses. It still throws for transport,
timeout, and cancellation failures because no HTTP response exists to return.

The raw API uses the authentication pipeline, including one refresh attempt
after an actual 401 response. If the refreshed request also returns 401, raw
execution returns that final 401 response; high-level execution converts it to
`D365AuthenticationException`.

```csharp
public sealed record D365Response(
    HttpStatusCode StatusCode,
    string RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    Uri RequestUri,
    string? RequestId,
    D365MutationOutcome MutationOutcome)
{
    public bool IsSuccessStatusCode =>
        (int)StatusCode is >= 200 and <= 299;

    public void EnsureSuccessStatusCode();
}

public sealed record D365Response<T>(
    HttpStatusCode StatusCode,
    T? Value,
    string RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    Uri RequestUri,
    string? RequestId,
    D365MutationOutcome MutationOutcome)
{
    public bool IsSuccessStatusCode =>
        (int)StatusCode is >= 200 and <= 299;

    public void EnsureSuccessStatusCode();
}
```

`IsSuccessStatusCode` is computed and cannot disagree with `StatusCode`.

## Exception Model

```csharp
public enum D365FailureKind
{
    Http,
    Authentication,
    Transport,
    Timeout,
    Serialization,
    Protocol
}

public enum D365MutationOutcome
{
    NotApplicable,
    NotSent,
    Rejected,
    SucceededOrAccepted,
    Unknown
}
```

The public hierarchy is:

```text
D365Exception
|- D365HttpException
|  `- D365AuthenticationException
|- D365TransportException
|- D365SerializationException
`- D365ProtocolException

D365OperationCanceledException : OperationCanceledException
```

Each applicable exception carries:

- `D365FailureKind FailureKind`;
- `HttpStatusCode? StatusCode`;
- `HttpMethod? Method`;
- `Uri? RequestUri`;
- `string? EntityName`;
- `string? ResponseBody`;
- `string? D365ErrorCode`;
- `string? D365ErrorMessage`;
- `string? RequestId`;
- `bool IsTransient`;
- `D365MutationOutcome MutationOutcome`;
- `TimeSpan? RetryAfter`;
- `long PartialRecordCount`;
- the original exception as `InnerException` when one exists.

Error response bodies stored in exceptions are capped at 64 KiB. Authorization,
access tokens, client secrets, cookies, and sensitive headers are never copied
into exception messages or logs.

## Mutation Outcome Classification

| Event | Read outcome | Mutation outcome |
|---|---|---|
| Failure before HTTP send begins | `NotApplicable` | `NotSent` |
| HTTP 2xx received | `NotApplicable` | `SucceededOrAccepted` |
| HTTP 400/401/403/404/409/412/422/429 | `NotApplicable` | `Rejected` |
| HTTP 408 received | `NotApplicable` | `Unknown` |
| HTTP 5xx received | `NotApplicable` | `Unknown` |
| Timeout or connection failure after send begins | `NotApplicable` | `Unknown` |
| Caller cancellation before send begins | `NotApplicable` | `NotSent` |
| Caller cancellation after mutation send begins | `NotApplicable` | `Unknown` |

Once `HttpClient.SendAsync()` has been invoked, transport failures are
classified conservatively as `Unknown` for mutations.

## Authentication and 401 Handling

The internal token-provider contract is:

```csharp
ValueTask<D365AccessToken> GetAccessTokenAsync(
    CancellationToken cancellationToken = default);

ValueTask<D365AccessToken> RefreshAccessTokenAsync(
    string rejectedAccessToken,
    CancellationToken cancellationToken = default);
```

The provider maintains one cached token per named client and uses a single
flight lock. Refresh compares the rejected token with the current token before
invalidating it. This prevents a delayed 401 from clearing a token another task
has already refreshed.

401 processing is bounded:

1. Send the request with the current token.
2. If an actual HTTP 401 is received, request a refreshed token using the
   rejected token as the compare value.
3. Clone and resend the request once.
4. If the second response is 401, return it to raw callers or throw
   `D365AuthenticationException` for high-level callers.

An actual 401 response is safe to retry once for mutations because the server
has rejected authentication. A timeout or missing response is not treated as
an authentication retry condition.

MSAL execution, ADFS token calls, lock waiting, and D365 requests all receive
the caller cancellation token.

## Retry Policy

Automatic retry is disabled by default.

```csharp
public sealed class D365RetryOptions
{
    public int MaxReadRetries { get; set; } = 0;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}
```

When explicitly enabled, only GET and HEAD may retry HTTP 408, 429, 500, 502,
503, and 504 or transient transport failures. The policy honors `Retry-After`
delta seconds and HTTP dates, otherwise using bounded exponential backoff with
jitter.

POST, PATCH, and DELETE never retry timeout, transport, 408, 429, or 5xx
failures. Callers recover using an application-specific idempotency or
correlation key and an exact follow-up query.

`IsTransient` describes whether the underlying failure may be temporary. It
does not mean a mutation is safe to retry.

## Timeout and Cancellation

Named D365 `HttpClient` instances use `Timeout.InfiniteTimeSpan`. The transport
creates a linked per-request timeout token so it can distinguish its own
timeout from caller cancellation.

```csharp
public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);
```

- Transport timeout raises `D365TransportException` with failure kind
  `Timeout`.
- Caller cancellation raises `D365OperationCanceledException`, which remains an
  `OperationCanceledException` for standard cancellation handling.
- Cancellation never implies that D365 rolled back or canceled a mutation.
- Mutation cancellation includes the classified mutation outcome.

## Query and Literal Construction

The query layer keeps OData expression formatting separate from URI encoding.

- Strings escape embedded single quotes as `''`.
- Reserved characters including `#`, `&`, `+`, `%`, whitespace, and Unicode are
  encoded at the query-URI layer.
- GUID values use the OData v4 unquoted GUID literal form.
- Date and time values use invariant round-trip formatting.
- Numeric values use invariant culture.
- Boolean formatting remains configurable as D365 `NoYesEnum` or OData literal.
- JSON property-name attributes continue to control field names.
- `$select`, `$filter`, `$orderby`, `$expand`, `$skip`, `$top`, `$count`, and
  `cross-company` are stored independently and combined deterministically.
- `cross-company=true` never removes a caller-specified `dataAreaId` filter.

`WhereClient()` remains an explicit opt-in API. Documentation must state that
it may scan multiple server pages and that `Take()` applies to matched client
records rather than the first server page. Normal `Where()` never silently
falls back to client-side filtering.

## Pagination

- Absolute and relative `@odata.nextLink` values are supported.
- A relative link resolves against the configured OData base URI.
- An absolute link must match the configured scheme, host, port, and base API
  path before the bearer token is sent.
- User-info components and cross-origin links are rejected.
- Normalized visited URIs detect next-link loops.
- `MaxPages` defaults to 10,000 and is configurable.
- Cancellation and request timeout apply to every page.
- Any page failure raises an exception with `PartialRecordCount`; no partial
  list is returned.
- Missing or malformed next-link values raise `D365ProtocolException`.

## Dependency Injection and Concurrency

Global static registration state is removed. Named client configuration belongs
to the service provider that registered it.

- `AddD365ODataClient()` registers named options and named HTTP clients.
- `ID365ClientFactory` is singleton within one service provider.
- The token-provider cache belongs to that factory instance and is keyed by
  client name.
- Root `ID365Client` instances are safe for parallel calls.
- Each `Entity<T>()` call creates a new non-thread-safe `D365Query<T>` builder.
- Multiple service providers can register the same client name independently.
- Duplicate names within one service provider fail during registration.
- ADFS token requests use `IHttpClientFactory` rather than constructing a new
  `HttpClient` per token acquisition.

## Logging

Information-level logs contain:

- HTTP method;
- entity name;
- status code;
- duration;
- page and record counts where applicable;
- D365 request or activity ID;
- transient and mutation-outcome classification.

Debug logs may contain a sanitized URI but not filter values or payload bodies.
Authorization, tokens, client secrets, cookies, and sensitive headers are
masked at every level. Request and response payloads are not logged by default.

Logging on an error path is best-effort. A logging-provider exception must not
replace the original D365 exception.

## D365 Error Parsing

For non-2xx responses, the transport buffers the body before creating an
exception. It extracts common Dataverse and D365 fields such as nested
`error.code` and `error.message`. Failure to parse an error envelope does not
change the HTTP failure classification; the capped raw body remains available.

The transport captures correlation headers including known D365 and Dataverse
request-ID variants. All response headers remain available through raw response
objects.

## Version 1 Removal and Migration

Version 2 removes:

- non-generic `Entity(string)` and `Entity(Enum)`;
- direct query and CRUD methods from `ID365Service`;
- `D365Service` and `ID365Service`;
- `D365ServiceFactory` and `ID365ServiceFactory`;
- string-return mutation methods;
- typed mutation methods that return `default(T)` on failure;
- recursive 401 handling;
- silent GET and count failure conversion.

The migration map is:

| Version 1 | Version 2 |
|---|---|
| `ID365Service` | `ID365Client` |
| `ID365ServiceFactory` | `ID365ClientFactory` |
| `Entity(string)` | `Entity<T>(string)` |
| mutation result string | `D365Response` or `D365Response<T>` |
| inspect error body as success value | catch typed exception |
| `null`/empty/`0` may mean failure | these values mean successful empty results only |

## Test Strategy

### Unit Tests

All HTTP behavior uses deterministic fake handlers. Required cases include:

- GET 200 with one row;
- GET 200 with an empty `value` array;
- GET 400, 401, 403, 404, 408, 429, 500, 502, 503, and 504;
- successful 401 refresh and second-401 failure;
- concurrent 401 responses causing only one effective token refresh;
- DNS, TLS, connection reset, timeout, and caller cancellation;
- malformed JSON, missing `value`, wrong `value` type, and 2xx error envelope;
- record deserialization failure;
- count zero, positive, overflow, missing count, and malformed count;
- two successful pages;
- page-two failure without a partial result;
- relative and absolute next links;
- cross-origin, wrong-base-path, malformed, and looping next links;
- maximum page guard;
- reserved query characters, quotes, GUIDs, dates, booleans, and Unicode;
- POST 200/201/202/204;
- PATCH and DELETE 200/204;
- mutation 4xx, 408, 429, 5xx, transport failure, timeout, and cancellation;
- successful mutation with malformed or missing typed response body;
- mutation retry prohibition;
- cancellation propagation through token and HTTP execution;
- logger failure preserving the root exception;
- independent named-client registrations in multiple service providers.

Unit tests run for `net8.0` and `net10.0`.

### Integration Tests

Integration tests are explicitly categorized and do not run merely because a
local settings file exists. They require an explicit environment switch and
external credentials. Cloud and On-Prem tests use isolated service providers
and report real request failures rather than converting them to zero counts.

### Documentation Samples

A compiled sample project exercises the public examples used by the official
documentation. Release CI builds the sample so API drift cannot silently break
the published usage snippets.

## Official Version 2 Documentation

Official documentation is completed after production behavior and tests are
stable so examples describe the shipped API. The documentation set is:

```text
README.md
CHANGELOG.md
docs/v2/getting-started.md
docs/v2/migration-from-v1.md
docs/v2/error-handling.md
docs/v2/mutations-and-responses.md
docs/v2/retry-timeout-cancellation.md
docs/v2/authentication-and-parallelism.md
docs/v2/query-and-pagination.md
docs/v2/security-and-logging.md
docs/v2/biowms-recovery-pattern.md
samples/FlintsLabs.D365.ODataClient.V2.Examples/
```

The migration guide includes compilable before-and-after examples for DI,
factories, entity queries, count, create, update, delete, exception handling,
and cancellation.

The BioWMS recovery guide documents:

- exact-row preflight queries;
- the difference between successful not-found and failed preflight;
- preserving the original `RunningNumber` or correlation key;
- handling `Unknown` mutation outcomes;
- avoiding automatic duplicate POST;
- reconciling state after timeout or cancellation.

The security guide explicitly documents the accepted certificate-validation
risk, sensitive URL and payload concerns, token masking, and the absence of
automatic mutation retry.

## Release and CI Gates

The `v2.0.0` tag may publish only after:

1. Unit tests pass on `net8.0` and `net10.0`.
2. Integration tests are excluded from normal package CI and pass in their
   explicit environment when requested.
3. The compiled sample project builds.
4. Release packaging succeeds with version `2.0.0`.
5. Package contents include the README, icon, XML documentation, and expected
   target-framework assemblies.
6. A temporary consumer project restores the generated package and compiles a
   representative query and mutation workflow.
7. No credentials, tokens, local settings, or sensitive response bodies are
   included in the package or git diff.
8. `CHANGELOG.md` and the migration guide list every breaking public API and
   behavior change.

The publish workflow runs unit tests before pack and push. Integration tests use
a separate manual or explicitly configured workflow. The existing `v*` tag
trigger remains the release entry point.

## Implementation Sequence

The implementation plan will decompose this design into independently
testable commits in this order:

1. Public response, outcome, and exception contracts.
2. Internal transport and deterministic transport tests.
3. Concurrent token refresh and service-provider-scoped registration.
4. Fail-closed GET, first, list, count, and long-count behavior.
5. Safe pagination and structured query URI construction.
6. Fail-closed POST, PATCH, and DELETE behavior.
7. Removal and renaming of version 1 legacy APIs.
8. Integration-test isolation and release CI gates.
9. Compiled samples and complete official version 2 documentation.
10. Package smoke test, version bump, and release readiness verification.

## Acceptance Criteria

- No production path converts request, timeout, protocol, serialization, or
  cancellation failure into a successful empty/default domain value.
- No paginated query returns partial records after a later-page failure.
- No mutation retries an ambiguous outcome automatically.
- One actual 401 response causes at most one refresh and one resend.
- Concurrent callers share tokens without refresh stampedes or stale-token
  invalidation races.
- Every public async network method accepts a cancellation token.
- Raw callers can inspect final status, body, headers, request ID, and mutation
  outcome.
- High-level callers receive typed exceptions with actionable diagnostics.
- Query values cannot truncate filters through reserved URI characters.
- Next links cannot send the bearer token outside the configured D365 endpoint.
- Multiple service providers do not share named-client registrations.
- Version 1 legacy APIs are absent from the version 2 public surface.
- Official documentation and compiled examples match the released package.
