# FlintsLabs D365 OData Client 2.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Release FlintsLabs.D365.ODataClient 2.0 with fail-closed reads and mutations, explicit raw responses, bounded authentication refresh, safe pagination, isolated DI registrations, and complete migration documentation.

**Architecture:** D365Client creates stateful D365Query<T> builders while an internal D365Transport owns every HTTP call, token attachment, timeout, 401 refresh, retry decision, response capture, and exception conversion. High-level APIs use an ensured transport path that throws typed exceptions; the raw API returns D365Response for every received HTTP response.

**Tech Stack:** C# 13, .NET 8/.NET 10, HttpClientFactory, MSAL, System.Text.Json, Microsoft Extensions DI/Options/Logging, xUnit, Moq.

## Global Constraints

- Target package version is 2.0.0.
- Remove D365Service, ID365Service, D365ServiceFactory, and ID365ServiceFactory.
- Never convert HTTP, transport, timeout, protocol, serialization, or cancellation failure into null, empty list, 0, empty string, or default(T).
- Never return partial records after pagination failure.
- Never retry POST, PATCH, or DELETE after timeout, transport, 408, 429, or 5xx failure.
- Retry an actual 401 response at most once after compare-and-refresh token handling.
- Every public asynchronous network method accepts CancellationToken.
- Keep current TLS certificate-validation behavior unchanged and document the accepted risk.
- Unit tests pass for net8.0 and net10.0 before release.
- Each implementation task ends in an independently testable commit.

---

## File Map

~~~text
FlintsLabs.D365.ODataClient/
  Exceptions/
  Models/
  OData/
  Transport/
  Services/D365Client.cs
  Services/D365ClientFactory.cs
  Services/D365Query.cs
  Services/ID365Client.cs
FlintsLabs.D365.ODataClient.Tests/
  TestInfrastructure/
  UnitTests/Authentication/
  UnitTests/DependencyInjection/
  UnitTests/OData/
  UnitTests/Queries/
  UnitTests/Transport/
docs/v2/
samples/FlintsLabs.D365.ODataClient.V2.Examples/
~~~

### Task 1: Public Result and Exception Contracts

**Files:**
- Create: FlintsLabs.D365.ODataClient/Models/D365FailureKind.cs
- Create: FlintsLabs.D365.ODataClient/Models/D365MutationOutcome.cs
- Create: FlintsLabs.D365.ODataClient/Models/D365Response.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365Exception.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365HttpException.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365AuthenticationException.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365TransportException.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365SerializationException.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365ProtocolException.cs
- Create: FlintsLabs.D365.ODataClient/Exceptions/D365OperationCanceledException.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport/PublicContractTests.cs

**Interfaces:**
- Produces: D365Response, D365Response<T>, D365FailureKind, D365MutationOutcome, and the public exception hierarchy.

- [ ] **Step 1: Write failing contract tests**

~~~csharp
[Fact]
public void Response_ComputesSuccessFromStatusCode()
{
    var response = new D365Response(
        HttpStatusCode.NoContent,
        string.Empty,
        new Dictionary<string, string[]>(),
        new Uri("https://example.test/data/Entities"),
        "request-1",
        D365MutationOutcome.SucceededOrAccepted);

    Assert.True(response.IsSuccessStatusCode);
}

[Fact]
public void EnsureSuccess_ThrowsTypedHttpException()
{
    var response = new D365Response(
        HttpStatusCode.BadRequest,
        "{\"error\":{\"code\":\"bad\",\"message\":\"invalid\"}}",
        new Dictionary<string, string[]>(),
        new Uri("https://example.test/data/Entities"),
        "request-2",
        D365MutationOutcome.Rejected);

    var exception = Assert.Throws<D365HttpException>(
        response.EnsureSuccessStatusCode);

    Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
}
~~~

- [ ] **Step 2: Run the tests and confirm missing-type failure**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~PublicContractTests
~~~

Expected: FAIL because the v2 contract types do not exist.

- [ ] **Step 3: Implement enums and the base exception**

~~~csharp
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

public class D365Exception : Exception
{
    public D365FailureKind FailureKind { get; }
    public HttpStatusCode? StatusCode { get; }
    public HttpMethod? Method { get; }
    public Uri? RequestUri { get; }
    public string? EntityName { get; }
    public string? ResponseBody { get; }
    public string? D365ErrorCode { get; }
    public string? D365ErrorMessage { get; }
    public string? RequestId { get; }
    public bool IsTransient { get; }
    public D365MutationOutcome MutationOutcome { get; }
    public TimeSpan? RetryAfter { get; }
    public long PartialRecordCount { get; internal set; }
}
~~~

Derived exceptions fix FailureKind in their constructors. D365HttpException
also exposes FromResponse(D365Response), which copies status, URI, body,
request ID, and mutation outcome. D365OperationCanceledException derives from
OperationCanceledException and exposes MutationOutcome.

- [ ] **Step 4: Implement response records**

~~~csharp
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

    public void EnsureSuccessStatusCode()
    {
        if (!IsSuccessStatusCode)
            throw D365HttpException.FromResponse(this);
    }
}
~~~

Implement the generic record with the same metadata plus T? Value.

- [ ] **Step 5: Verify both target frameworks**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~PublicContractTests
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter FullyQualifiedName~PublicContractTests
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/Models FlintsLabs.D365.ODataClient/Exceptions FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport/PublicContractTests.cs
git commit -m "feat!: add v2 response and exception contracts"
~~~

### Task 2: Deterministic HTTP Test Infrastructure

**Files:**
- Create: FlintsLabs.D365.ODataClient.Tests/TestInfrastructure/StubHttpMessageHandler.cs
- Create: FlintsLabs.D365.ODataClient.Tests/TestInfrastructure/StubTokenProvider.cs
- Create: FlintsLabs.D365.ODataClient.Tests/TestInfrastructure/ThrowingLogger.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/TestInfrastructureTests.cs

**Interfaces:**
- Produces: queued HTTP responses/exceptions, request capture, token refresh counters, and a logger that throws.

- [ ] **Step 1: Implement the queued handler**

~~~csharp
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken,
        Task<HttpResponseMessage>>> _steps = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Enqueue(HttpStatusCode statusCode, string body = "")
    {
        _steps.Enqueue((_, _) => Task.FromResult(
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    body, Encoding.UTF8, "application/json")
            }));
    }

    public void EnqueueException(Exception exception) =>
        _steps.Enqueue((_, _) =>
            Task.FromException<HttpResponseMessage>(exception));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (_steps.Count == 0)
            throw new InvalidOperationException("No queued HTTP response.");

        return await _steps.Dequeue()(request, cancellationToken);
    }
}
~~~

- [ ] **Step 2: Implement token and logger stubs**

StubTokenProvider returns initial-token, refreshes to refreshed-token, and counts calls. ThrowingLogger throws from Log and returns a no-op scope.

- [ ] **Step 3: Test response order, exception propagation, request capture, refresh count, and logger failure**

- [ ] **Step 4: Run tests**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~TestInfrastructureTests
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient.Tests/TestInfrastructure FlintsLabs.D365.ODataClient.Tests/UnitTests/TestInfrastructureTests.cs
git commit -m "test: add deterministic D365 HTTP infrastructure"
~~~

### Task 3: Central Raw and Ensured Transport

**Files:**
- Create: FlintsLabs.D365.ODataClient/Transport/D365Request.cs
- Create: FlintsLabs.D365.ODataClient/Transport/ID365Transport.cs
- Create: FlintsLabs.D365.ODataClient/Transport/D365ErrorParser.cs
- Create: FlintsLabs.D365.ODataClient/Transport/D365LogSanitizer.cs
- Create: FlintsLabs.D365.ODataClient/Transport/D365Transport.cs
- Create: FlintsLabs.D365.ODataClient/Extensions/D365RetryOptions.cs
- Modify: FlintsLabs.D365.ODataClient/Extensions/D365ClientOptions.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport/D365TransportTests.cs

**Interfaces:**
- Consumes: Task 1 contracts and Task 2 stubs.
- Produces: SendRawAsync and SendEnsuredAsync.

- [ ] **Step 1: Write failing tests**

Test raw 200/204/400/500 responses, ensured non-2xx exceptions, D365 error code/message parsing, request ID and headers, transient classification, 64 KiB exception-body truncation, timeout, HttpRequestException, and cancellation.

- [ ] **Step 2: Confirm failure**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365TransportTests
~~~

- [ ] **Step 3: Implement a cloneable request description**

~~~csharp
internal sealed record D365Request(
    HttpMethod Method,
    string RelativeOrAbsoluteUrl,
    string? JsonPayload,
    string? EntityName,
    IReadOnlyDictionary<string, string> Headers)
{
    public bool IsMutation =>
        Method == HttpMethod.Post ||
        Method == HttpMethod.Patch ||
        Method == HttpMethod.Delete;

    public HttpRequestMessage CreateMessage(string token)
    {
        var request = new HttpRequestMessage(
            Method, RelativeOrAbsoluteUrl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var header in Headers)
            request.Headers.TryAddWithoutValidation(
                header.Key, header.Value);

        if (JsonPayload is not null)
            request.Content = new StringContent(
                JsonPayload, Encoding.UTF8, "application/json");

        return request;
    }
}
~~~

Every attempt creates a fresh request and content instance.

- [ ] **Step 4: Add transport options**

~~~csharp
public sealed class D365RetryOptions
{
    public int MaxReadRetries { get; set; }
    public TimeSpan BaseDelay { get; set; } =
        TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } =
        TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}

public TimeSpan RequestTimeout { get; set; } =
    TimeSpan.FromSeconds(100);
public int MaxErrorBodyBytes { get; set; } = 64 * 1024;
public int MaxPages { get; set; } = 10_000;
public D365RetryOptions Retry { get; set; } = new();
~~~

- [ ] **Step 5: Implement transport interfaces**

~~~csharp
internal interface ID365Transport
{
    Task<D365Response> SendRawAsync(
        D365Request request,
        CancellationToken cancellationToken);

    Task<D365Response> SendEnsuredAsync(
        D365Request request,
        CancellationToken cancellationToken);
}
~~~

SendRawAsync obtains a token, creates a linked timeout token, sends with ResponseHeadersRead, reads the body, captures headers/request ID, disposes request/response, and returns every received HTTP response. SendEnsuredAsync converts the final non-2xx response into a typed exception.

- [ ] **Step 6: Classify failures**

Treat 408/429/500/502/503/504 as transient. Mutation 2xx is SucceededOrAccepted; 400/401/403/404/409/412/422/429 is Rejected; 408/5xx/transport after SendAsync begins is Unknown. Token failure before send is NotSent.

- [ ] **Step 7: Verify**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365TransportTests
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter FullyQualifiedName~D365TransportTests
~~~

- [ ] **Step 8: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/Transport FlintsLabs.D365.ODataClient/Extensions/D365ClientOptions.cs FlintsLabs.D365.ODataClient/Extensions/D365RetryOptions.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport
git commit -m "feat!: centralize D365 HTTP transport"
~~~

### Task 4: Concurrent Token Refresh and Bounded 401

**Files:**
- Create: FlintsLabs.D365.ODataClient/Models/D365AccessToken.cs
- Modify: FlintsLabs.D365.ODataClient/Services/ID365AccessTokenProvider.cs
- Delete: FlintsLabs.D365.ODataClient/Services/IAccessTokenProvider.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365AccessTokenProvider.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365Service.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Modify: FlintsLabs.D365.ODataClient/Transport/D365Transport.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Authentication/D365AuthenticationTests.cs

**Interfaces:**
- Produces: cancellable token acquisition, compare-and-refresh, and one 401 resend.

- [ ] **Step 1: Write tests for 401-success, second-401 failure, mutation 401 retry, cancellation, and concurrent stale-token rejection**

- [ ] **Step 2: Introduce the token contract**

~~~csharp
public sealed record D365AccessToken(
    string Value,
    DateTimeOffset ExpiresOn);

public interface ID365AccessTokenProvider
{
    ValueTask<D365AccessToken> GetAccessTokenAsync(
        CancellationToken cancellationToken = default);

    ValueTask<D365AccessToken> RefreshAccessTokenAsync(
        string rejectedAccessToken,
        CancellationToken cancellationToken = default);
}
~~~

- [ ] **Step 3: Implement compare-and-refresh under the semaphore**

Clear the cached token only when its value equals rejectedAccessToken. If a concurrent caller already installed another valid token, return it without another authority request.

- [ ] **Step 4: Propagate cancellation**

~~~csharp
await _tokenLock.WaitAsync(cancellationToken);
await authBuilder.AcquireTokenForClient(scopes)
    .ExecuteAsync(cancellationToken);
await httpClient.PostAsync(
    tokenEndpoint, content, cancellationToken);
~~~

Update the temporary version 1 service and query request builders to use the
Value property of D365AccessToken so the repository remains buildable until
the legacy service is removed and the query is migrated to transport.

- [ ] **Step 5: Add a non-recursive one-time 401 resend**

Capture the rejected token, refresh, create a fresh D365Request message, resend once, and never enter the branch again.

- [ ] **Step 6: Verify**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "FullyQualifiedName~D365AuthenticationTests|FullyQualifiedName~D365TransportTests"
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter "FullyQualifiedName~D365AuthenticationTests|FullyQualifiedName~D365TransportTests"
~~~

- [ ] **Step 7: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/Models/D365AccessToken.cs FlintsLabs.D365.ODataClient/Services FlintsLabs.D365.ODataClient/Transport/D365Transport.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Authentication
git commit -m "feat!: add bounded concurrent token refresh"
~~~

### Task 5: Root Client and Isolated Named Registrations

**Files:**
- Create: FlintsLabs.D365.ODataClient/Services/ID365Client.cs
- Create: FlintsLabs.D365.ODataClient/Services/D365Client.cs
- Create: FlintsLabs.D365.ODataClient/Services/D365ClientFactory.cs
- Modify: FlintsLabs.D365.ODataClient/Extensions/ServiceCollectionExtensions.cs
- Delete: FlintsLabs.D365.ODataClient/Services/D365Service.cs
- Delete: FlintsLabs.D365.ODataClient/Services/ID365Service.cs
- Delete: FlintsLabs.D365.ODataClient/Services/D365ServiceFactory.cs
- Modify: FlintsLabs.D365.ODataClient.Tests/Fixtures/IntegrationTestBase.cs
- Modify: FlintsLabs.D365.ODataClient.Tests/IntegrationTests/ConnectivityTests.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/DependencyInjection/D365RegistrationTests.cs

**Interfaces:**
- Produces: ID365Client, ID365ClientFactory, Entity<T>, and raw SendAsync.

- [ ] **Step 1: Write tests that two service providers can reuse Cloud independently and one provider rejects duplicate Cloud registration**

- [ ] **Step 2: Add root interfaces**

~~~csharp
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

public interface ID365ClientFactory
{
    ID365Client GetClient();
    ID365Client GetClient(string name);
}
~~~

- [ ] **Step 3: Remove global static registration**

Store immutable registration descriptors in the current IServiceCollection, use named options and named HttpClients, and cache clients/token providers only inside the provider-owned singleton factory.

- [ ] **Step 4: Configure HTTP lifetime**

Set D365 HttpClient.Timeout to InfiniteTimeSpan. Preserve the current certificate callback for D365 and ADFS exactly as approved.

- [ ] **Step 5: Implement raw sending and Entity<T> creation**

Serialize optional raw payload once and delegate to SendRawAsync. Return a new D365Query<T> for every Entity<T> call.

- [ ] **Step 6: Remove the old root service and migrate integration compilation**

Delete the version 1 service/interface/factory files. Change integration
fixtures to resolve ID365ClientFactory and call GetClient(scope.ToString()).
Live test execution remains unchanged until Task 13 isolates it.

- [ ] **Step 7: Run registration tests twice**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365RegistrationTests
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365RegistrationTests
~~~

- [ ] **Step 8: Commit**

~~~bash
git add -A FlintsLabs.D365.ODataClient/Services FlintsLabs.D365.ODataClient/Extensions/ServiceCollectionExtensions.cs FlintsLabs.D365.ODataClient.Tests/Fixtures FlintsLabs.D365.ODataClient.Tests/IntegrationTests FlintsLabs.D365.ODataClient.Tests/UnitTests/DependencyInjection
git commit -m "feat!: add isolated v2 D365 clients"
~~~

### Task 6: Fail-Closed First and List Reads

**Files:**
- Create: FlintsLabs.D365.ODataClient/OData/ODataCollectionPage.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365ReadBehaviorTests.cs

**Interfaces:**
- Produces: strict OData collection parsing and fail-closed FirstOrDefaultAsync/ToListAsync.

- [ ] **Step 1: Test 200-one-row, 200-empty, all relevant non-2xx, transport, timeout, cancellation, malformed JSON, missing value, wrong value type, 2xx error envelope, and record deserialization failure**

- [ ] **Step 2: Add strict page model**

~~~csharp
internal sealed record ODataCollectionPage<T>(
    IReadOnlyList<T> Records,
    string? NextLink,
    long? Count);
~~~

Require a JSON object and value array. Wrap JsonException as D365SerializationException and structural violations as D365ProtocolException.

- [ ] **Step 3: Delete the nullable GET helper**

Replace GetResponseJsonDocumentAsync with ensured transport. Do not catch request/parser exceptions into successful values.

- [ ] **Step 4: Keep null semantics narrow**

FirstOrDefaultAsync applies Take(1), awaits ToListAsync, and returns null only when a valid successful collection has no record.

- [ ] **Step 5: Verify reads and existing translators**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "FullyQualifiedName~D365ReadBehaviorTests|FullyQualifiedName~BooleanTests|FullyQualifiedName~CoalesceTests|FullyQualifiedName~ListContainsTests"
~~~

- [ ] **Step 6: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/OData/ODataCollectionPage.cs FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365ReadBehaviorTests.cs
git commit -m "fix!: make D365 reads fail closed"
~~~

### Task 7: Safe Pagination

**Files:**
- Create: FlintsLabs.D365.ODataClient/OData/ODataNextLinkValidator.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365PaginationTests.cs

**Interfaces:**
- Produces: same-endpoint link validation, loop detection, MaxPages, and all-or-error results.

- [ ] **Step 1: Test two pages, page-two 500, relative/absolute links, cross-origin, wrong base path, malformed link, loop, MaxPages, and page-two cancellation**

- [ ] **Step 2: Implement URI validation**

Resolve relative links against GetBaseUrl. Absolute links require equal scheme, host, and port and a path under the normalized configured API base path. Reject user-info and non-HTTP(S) schemes.

- [ ] **Step 3: Implement loop/page guards**

Normalize absolute page URIs in HashSet<string> and throw D365ProtocolException before exceeding MaxPages.

- [ ] **Step 4: Preserve partial diagnostics only**

On D365Exception, set PartialRecordCount to records.Count and rethrow the same exception. Never return records after failure.

- [ ] **Step 5: Verify both targets**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365PaginationTests
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter FullyQualifiedName~D365PaginationTests
~~~

- [ ] **Step 6: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/OData/ODataNextLinkValidator.cs FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365PaginationTests.cs
git commit -m "feat!: validate D365 pagination"
~~~

### Task 8: Strict Count and Long Count

**Files:**
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Modify: FlintsLabs.D365.ODataClient/OData/ODataCollectionPage.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365CountTests.cs

**Interfaces:**
- Produces: checked CountAsync and full-width LongCountAsync.

- [ ] **Step 1: Test zero, positive, int overflow, missing count, malformed count, non-2xx, transport failure, and client-filter page failure**

- [ ] **Step 2: Parse @odata.count strictly as Int64**

Add D365ProtocolException.MissingOrInvalidCount(D365Response), which copies the
response diagnostics and uses MutationOutcome.NotApplicable, then call it from
the parser:

~~~csharp
if (!root.TryGetProperty("@odata.count", out var count) ||
    count.ValueKind != JsonValueKind.Number ||
    !count.TryGetInt64(out var value))
{
    throw D365ProtocolException.MissingOrInvalidCount(response);
}
~~~

- [ ] **Step 3: Implement public methods**

~~~csharp
public async Task<int> CountAsync(
    CancellationToken cancellationToken = default) =>
    checked((int)await LongCountAsync(cancellationToken));
~~~

LongCountAsync requests $count=true and $top=0. WhereClient scans all pages and propagates every failure.

- [ ] **Step 4: Verify**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter FullyQualifiedName~D365CountTests
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter FullyQualifiedName~D365CountTests
~~~

- [ ] **Step 5: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient/OData/ODataCollectionPage.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365CountTests.cs
git commit -m "feat!: add strict count queries"
~~~

### Task 9: Structured and Encoded OData Queries

**Files:**
- Create: FlintsLabs.D365.ODataClient/OData/ODataLiteralFormatter.cs
- Create: FlintsLabs.D365.ODataClient/OData/ODataQueryBuilder.cs
- Modify: FlintsLabs.D365.ODataClient/Expressions/D365ExpressionVisitor.cs
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/OData/ODataQueryEncodingTests.cs

**Interfaces:**
- Produces: deterministic query options and safe literal/URI encoding.

- [ ] **Step 1: Test O'Brien, DIM#001, A&B+C%20, Thai Unicode, GUID, date, bool, cross-company, and dataAreaId together**

- [ ] **Step 2: Implement literal formatting**

Format null, string with doubled quote, configurable bool, unquoted GUID, invariant numeric, DateTime, DateTimeOffset, and enum values. Do not URI-encode in the formatter.

- [ ] **Step 3: Implement structured options**

~~~csharp
internal sealed class ODataQueryBuilder
{
    private readonly Dictionary<string, string> _single =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _expands = [];

    public void Set(string name, string value) =>
        _single[name] = value;

    public void AddExpand(string value) =>
        _expands.Add(value);

    public string Build(string entity, bool crossCompany);
}
~~~

Build encoded query key/value components and never let # create a URI fragment.

- [ ] **Step 4: Adapt the expression visitor**

Use ODataLiteralFormatter, preserve coalesce/Contains/JsonPropertyName behavior, and replace Console.WriteLine fallback with a clear translation exception.

- [ ] **Step 5: Verify all query tests**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "FullyQualifiedName~OData|FullyQualifiedName~BooleanTests|FullyQualifiedName~CoalesceTests|FullyQualifiedName~ExpandTests|FullyQualifiedName~OrderByTests|FullyQualifiedName~ListContainsTests"
~~~

- [ ] **Step 6: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/OData FlintsLabs.D365.ODataClient/Expressions/D365ExpressionVisitor.cs FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/OData
git commit -m "feat!: build encoded OData queries"
~~~

### Task 10: Fail-Closed Mutations

**Files:**
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Modify: FlintsLabs.D365.ODataClient/Transport/D365Transport.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365MutationTests.cs

**Interfaces:**
- Produces: response-returning POST/PATCH/DELETE with typed POST parsing and outcome diagnostics.

- [ ] **Step 1: Test POST 200/201/202/204, PATCH/DELETE 200/204, every non-2xx class, typed valid/empty/malformed body, timeout, transport, cancellation, no ambiguous retry, 401 retry, OdataKey, and AddIdentity**

- [ ] **Step 2: Replace signatures**

All mutation overloads return D365Response or D365Response<TResponse>, accept CancellationToken, serialize once into D365Request, and use ensured transport.

- [ ] **Step 3: Parse typed success**

Add D365ProtocolException.EmptyTypedMutationBody(D365Response),
D365ProtocolException.EmptyTypedMutationValue(D365Response), and
D365SerializationException.ForSuccessfulMutation(D365Response, JsonException).
Each factory copies response diagnostics and preserves SucceededOrAccepted.

~~~csharp
if (string.IsNullOrWhiteSpace(response.RawBody))
    throw D365ProtocolException.EmptyTypedMutationBody(response);

try
{
    var value = JsonSerializer.Deserialize<TResponse>(
        response.RawBody);

    if (value is null)
        throw D365ProtocolException.EmptyTypedMutationValue(
            response);

    return new D365Response<TResponse>(
        response.StatusCode,
        value,
        response.RawBody,
        response.Headers,
        response.RequestUri,
        response.RequestId,
        response.MutationOutcome);
}
catch (JsonException exception)
{
    throw D365SerializationException.ForSuccessfulMutation(
        response, exception);
}
~~~

Both typed-body failures carry SucceededOrAccepted.

- [ ] **Step 4: Assert no ambiguous mutation retry**

For 408/429/5xx/transport/timeout, handler request count equals one. For actual 401 then success, count equals two.

- [ ] **Step 5: Verify**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "FullyQualifiedName~D365MutationTests|FullyQualifiedName~KeyWhereUpdateTests|FullyQualifiedName~DataversePatchTests"
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter "FullyQualifiedName~D365MutationTests|FullyQualifiedName~KeyWhereUpdateTests|FullyQualifiedName~DataversePatchTests"
~~~

- [ ] **Step 6: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient/Transport/D365Transport.cs FlintsLabs.D365.ODataClient.Tests/UnitTests/Queries/D365MutationTests.cs
git commit -m "fix!: make D365 mutations fail closed"
~~~

### Task 11: Opt-In Read Retry and Safe Logging

**Files:**
- Modify: FlintsLabs.D365.ODataClient/Extensions/D365ClientOptions.cs
- Modify: FlintsLabs.D365.ODataClient/Extensions/D365RetryOptions.cs
- Modify: FlintsLabs.D365.ODataClient/Transport/D365Transport.cs
- Modify: FlintsLabs.D365.ODataClient/Transport/D365LogSanitizer.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport/D365RetryTests.cs
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport/D365LoggingTests.cs

**Interfaces:**
- Produces: bounded GET/HEAD retry and non-sensitive structured logs.

- [ ] **Step 1: Test default zero retry, opt-in 408/429/500/502/503/504 retry, Retry-After delta/date, max delay, cancellation during delay, and no mutation retry**

- [ ] **Step 2: Validate retry options**

Reject negative retry counts, non-positive delays, or BaseDelay greater than
MaxDelay during client registration. Keep the options introduced in Task 3:

~~~csharp
public sealed class D365RetryOptions
{
    public int MaxReadRetries { get; set; }
    public TimeSpan BaseDelay { get; set; } =
        TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } =
        TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}
~~~

- [ ] **Step 3: Implement fresh-request read retries**

Retry only GET/HEAD. Honor Retry-After before bounded exponential delay and jitter. Propagate cancellation during delay.

- [ ] **Step 4: Test logging**

Assert method/entity/status/duration/request ID are present and bearer token/filter values/client secret/payload are absent. ThrowingLogger must not replace the root D365 exception.

- [ ] **Step 5: Implement sanitized best-effort logging**

Log route shape and query-option names, not values. Keep header masking centralized. Catch only logger invocation failure.

- [ ] **Step 6: Verify and commit**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "FullyQualifiedName~D365RetryTests|FullyQualifiedName~D365LoggingTests"
git add FlintsLabs.D365.ODataClient/Extensions/D365ClientOptions.cs FlintsLabs.D365.ODataClient/Transport FlintsLabs.D365.ODataClient.Tests/UnitTests/Transport
git commit -m "feat: add read retry and safe diagnostics"
~~~

### Task 12: Audit Version 1 Removal and Public API

**Files:**
- Modify: FlintsLabs.D365.ODataClient/Services/D365Query.cs
- Modify: existing unit tests that construct D365Query<T>
- Test: FlintsLabs.D365.ODataClient.Tests/UnitTests/PublicApiRemovalTests.cs

**Interfaces:**
- Produces: a v2 assembly with no legacy service API.

- [ ] **Step 1: Add reflection tests that removed type names are absent and all network methods accept CancellationToken**

- [ ] **Step 2: Remove transitional query constructors and migrate tests to the v2 client or internal transport factory**

- [ ] **Step 3: Run all unit tests**

~~~bash
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "Category!=Integration"
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net10.0 --filter "Category!=Integration"
~~~

- [ ] **Step 4: Commit**

~~~bash
git add -A FlintsLabs.D365.ODataClient/Services/D365Query.cs FlintsLabs.D365.ODataClient.Tests
git commit -m "refactor!: remove D365Service v1 API"
~~~

### Task 13: Integration Isolation and Release CI

**Files:**
- Modify: FlintsLabs.D365.ODataClient.Tests/Fixtures/IntegrationTestBase.cs
- Modify: FlintsLabs.D365.ODataClient.Tests/IntegrationTests/ConnectivityTests.cs
- Modify: .github/workflows/publish-nuget.yml
- Create: .github/workflows/integration-tests.yml

**Interfaces:**
- Produces: deterministic package CI and explicit live verification.

- [ ] **Step 1: Add Category=Integration and require D365_RUN_INTEGRATION_TESTS=true before reading live settings**

- [ ] **Step 2: Verify unit mode makes no live request**

~~~bash
unset D365_RUN_INTEGRATION_TESTS
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -f net8.0 --filter "Category!=Integration"
~~~

- [ ] **Step 3: Update publish CI**

Install .NET 8 and 10 SDKs, restore, test both unit targets, build samples, pack the tag version, inspect package contents, then push.

- [ ] **Step 4: Add workflow_dispatch integration CI**

Run only Category=Integration with environment secrets and D365_RUN_INTEGRATION_TESTS=true. Never print configuration values.

- [ ] **Step 5: Commit**

~~~bash
git add FlintsLabs.D365.ODataClient.Tests/Fixtures FlintsLabs.D365.ODataClient.Tests/IntegrationTests .github/workflows
git commit -m "ci: isolate live tests and gate publishing"
~~~

### Task 14: Compiled Version 2 Examples

**Files:**
- Create: samples/FlintsLabs.D365.ODataClient.V2.Examples/FlintsLabs.D365.ODataClient.V2.Examples.csproj
- Create: samples/FlintsLabs.D365.ODataClient.V2.Examples/Program.cs
- Create: samples/FlintsLabs.D365.ODataClient.V2.Examples/Models/EgrHead.cs

**Interfaces:**
- Produces: compilable DI, read, update, raw, cancellation, and uncertain-outcome examples.

- [ ] **Step 1: Create a net8.0 console project referencing the library**

- [ ] **Step 2: Add examples**

~~~csharp
var existing = await dataverse
    .Entity<EgrHead>("rvl_egrheads")
    .Where(x => x.Id == headId)
    .FirstOrDefaultAsync(cancellationToken);

try
{
    await dataverse.Entity<EgrHead>("rvl_egrheads")
        .Where(x => x.Id == headId)
        .UpdateAsync(
            new { rvl_wmsstatus = false },
            cancellationToken);
}
catch (D365TransportException exception)
    when (exception.MutationOutcome ==
          D365MutationOutcome.Unknown)
{
    // Reconcile by exact correlation key before retry.
}
~~~

Also compile named-factory, raw SendAsync, typed POST, and cancellation examples.

- [ ] **Step 3: Build without network access**

~~~bash
dotnet build samples/FlintsLabs.D365.ODataClient.V2.Examples/FlintsLabs.D365.ODataClient.V2.Examples.csproj -c Release
~~~

- [ ] **Step 4: Commit**

~~~bash
git add samples
git commit -m "docs: add compiled v2 examples"
~~~

### Task 15: Official Version 2 Documentation

**Files:**
- Rewrite: README.md
- Create: CHANGELOG.md
- Create: docs/v2/getting-started.md
- Create: docs/v2/migration-from-v1.md
- Create: docs/v2/error-handling.md
- Create: docs/v2/mutations-and-responses.md
- Create: docs/v2/retry-timeout-cancellation.md
- Create: docs/v2/authentication-and-parallelism.md
- Create: docs/v2/query-and-pagination.md
- Create: docs/v2/security-and-logging.md
- Create: docs/v2/biowms-recovery-pattern.md

**Interfaces:**
- Consumes: final signatures and compiled examples.
- Produces: public documentation and migration guidance.

- [ ] **Step 1: Rewrite README**

Cover installation, Azure AD/ADFS, named clients, ID365Client injection, reads, mutations, exceptions, cancellation, guide links, frameworks, and TLS caveat.

- [ ] **Step 2: Write migration guide**

Include before/after mappings for ID365Service, factories, generic Entity<T>, D365Response, typed exceptions, Count behavior, and CancellationToken.

- [ ] **Step 3: Write operational guides**

Document raw versus ensured behavior, all exception properties, outcomes, 401 refresh, retry, Retry-After, timeout/cancellation, pagination validation, and logging redaction.

- [ ] **Step 4: Write BioWMS recovery guide**

Show successful not-found versus failed preflight, no POST after failed GET, reuse of RunningNumber/correlation key, reconciliation after Unknown, and exact query before caller-controlled retry.

- [ ] **Step 5: Write changelog and validate**

~~~bash
dotnet build samples/FlintsLabs.D365.ODataClient.V2.Examples/FlintsLabs.D365.ODataClient.V2.Examples.csproj -c Release
rg -n "ID365Service|D365ServiceFactory" README.md docs/v2
~~~

Old names may appear only in migration text.

- [ ] **Step 6: Commit**

~~~bash
git add README.md CHANGELOG.md docs/v2
git commit -m "docs!: publish official v2 documentation"
~~~

### Task 16: Version, Package, and Release Verification

**Files:**
- Modify: FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj
- Local-only output: artifacts/nupkg/

**Interfaces:**
- Produces: verified 2.0.0 package ready for explicit push/tag approval.

- [ ] **Step 1: Set version and package metadata**

Set Version to 2.0.0, enable XML docs, and replace release notes with the fail-closed summary and migration link.

- [ ] **Step 2: Run the complete matrix**

~~~bash
dotnet clean
dotnet restore
dotnet build FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj -c Release -f net8.0 --no-restore
dotnet build FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj -c Release -f net10.0 --no-restore
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -c Release -f net8.0 --no-restore --filter "Category!=Integration"
dotnet test FlintsLabs.D365.ODataClient.Tests/FlintsLabs.D365.ODataClient.Tests.csproj -c Release -f net10.0 --no-restore --filter "Category!=Integration"
dotnet build samples/FlintsLabs.D365.ODataClient.V2.Examples/FlintsLabs.D365.ODataClient.V2.Examples.csproj -c Release --no-restore
~~~

- [ ] **Step 3: Pack and inspect**

~~~bash
rm -rf artifacts/nupkg
dotnet pack FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj -c Release --no-build -o artifacts/nupkg -p:Version=2.0.0
unzip -l artifacts/nupkg/FlintsLabs.D365.ODataClient.2.0.0.nupkg
~~~

Confirm README, icon, XML docs, and both framework assemblies are present, with no settings or credentials.

- [ ] **Step 4: Test the packed package**

Create a temporary console app, add artifacts/nupkg as a source, install exact 2.0.0, compile an ID365Client read and response-based mutation, and delete the temporary app.

- [ ] **Step 5: Final review**

~~~bash
git status --short
git diff --check
git diff v1.2.27...HEAD --stat
rg -n "return default|return string.Empty|return count \?\? 0|ServerCertificateCustomValidationCallback" FlintsLabs.D365.ODataClient
~~~

The certificate callback remains only because the design explicitly keeps it. Unsafe network default-return paths must be absent.

- [ ] **Step 6: Commit release metadata**

~~~bash
git add FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj
git commit -m "chore!: prepare 2.0.0 release"
~~~

- [ ] **Step 7: Stop before external actions**

Report test/package evidence and wait for explicit approval before creating v2.0.0 or pushing commits/tags. The tag triggers NuGet publication.

## Plan Self-Review

- Every design requirement maps to a task.
- Public names and signatures match across code, tests, examples, and docs.
- Every behavior change starts with a failing test.
- Integration tests are excluded from normal package CI.
- TLS behavior remains unchanged and documented as an accepted risk.
- No task pushes or tags without explicit approval.
