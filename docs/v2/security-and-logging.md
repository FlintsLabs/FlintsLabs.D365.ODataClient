# Security and Logging

This guide defines TLS validation, what the package redacts, and what remains the caller's responsibility. The security fixes below are included in version 2.2.1; version 2.2.0 remains affected and installed applications must upgrade.

## TLS Validation (2.2.1)

Both named clients (`D365Endpoint_<name>` and `D365Auth_<name>`) now use the platform HTTP handler with normal certificate validation. The package does not install a custom validation callback or provide an option to accept invalid certificates.

The client rejects untrusted certificate chains, certificates that do not match the requested hostname, and certificates outside their validity period. The authentication client is used for ADFS token requests. Azure AD and Managed Identity token acquisition use MSAL's separate HTTP transport; this patch does not change that transport.

For private-CA or on-premises endpoints, install the approved CA trust chain on the application host/container and serve a matching, unexpired certificate with the necessary intermediate certificates. Deployments that depended on accepting invalid certificates will now fail the TLS handshake. Fix certificate/trust configuration rather than reintroducing a bypass. Applications can override `IHttpClientFactory` configuration, so review any application-owned handler customization as well.

Local HTTPS regression tests exercise both named clients with a trusted certificate, an untrusted issuer, a hostname mismatch, and an expired certificate. The test CA is trusted only in the test handler's in-memory chain policy; no machine trust-store changes or external services are required. Negative cases also verify that no HTTP request reaches the server.

## Endpoint and Redirect Policy (2.2.1)

- The effective API endpoint (`OrganizationUrl`, otherwise `Resource`) must be an absolute HTTPS URL without user information, query strings, or fragments. When `OrganizationUrl` is present, `Resource` is an OAuth audience and need not be an HTTPS network URL (for example, an ADFS URN).
- ADFS `TokenEndpoint` must be absolute HTTPS without user information or fragments. It may use a different host from the API. This check is specific to ADFS; Azure AD and Managed Identity acquire tokens through MSAL.
- Invalid endpoint configuration raises `InvalidOperationException` during registration/configuration. Runtime validation also protects options configured through other paths. Validation messages do not echo the rejected URL or credentials.
- The shared transport resolves relative/root-relative/absolute request URLs against the configured API base URL, checks HTTPS plus the same normalized host and port, and sends that exact absolute URI. This covers raw calls, queries, mutations, and pagination. User information/fragments and foreign origins are rejected before acquiring tokens or sending HTTP, with `D365ProtocolException` (`NotSent` for mutations). Pagination retains its stricter API-base-path check.
- Raw calls may still use same-origin paths outside `/data/`, for example `/api/services/...`. For another origin, register another named D365 client. Changing only the named `HttpClient.BaseAddress` does not change the configured API origin.
- Query `AddHeader("Host", ...)` overrides are rejected before token acquisition. .NET uses this header for HTTP authority, TLS SNI, and certificate hostname validation; allowing it would undermine the configured origin. See [Microsoft's SNI documentation](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-sni).
- Both named clients disable automatic redirects, including same-origin redirects. Raw `SendAsync` returns the original 3xx response, high-level calls throw `D365HttpException`, and ADFS token acquisition fails with `HttpRequestException`. This prevents automatic forwarding of credentials or mutation bodies on 307/308. Configure the final URL directly; do not blindly replay a response's `Location` header.

These protections are independent of authentication mode and build configuration. Debug/local Client Secret flows must also use valid HTTPS endpoints. There is no package switch to bypass them. Application-owned replacement handlers/delegating handlers or direct use of `IHttpClientFactory` are trusted in-process customizations; review them for destination rewriting, automatic redirects, TLS bypasses, and logging.

The PR CI matrix exercises non-integration tests on Windows/Linux with .NET 8/10, including local HTTPS redirect tests. On Windows, test server keys are imported temporarily into the user key container for Schannel compatibility and disposed after each test; certificates are not installed into certificate stores, and custom-root trust stays in the handler. See [Microsoft's Schannel guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/sslstream-troubleshooting#handshake-failed-with-ephemeral-keys). Deployment-specific trust chains, proxies, and live D365 permissions still need environment verification. The unused vulnerable `Microsoft.AspNetCore.WebUtilities` reference has been removed; use `dotnet list package --vulnerable --include-transitive` to audit the consuming application's own dependency graph as well.

## Published Version 2.2.0

Version 2.2.0 (and the preceding 2.0.0/2.1.0 releases) registers both named clients with a callback that returns `true` for every server certificate. It bypasses certificate-chain, hostname, and validity checks. An active attacker on the network path could impersonate the D365 or ADFS endpoint and intercept tokens, client credentials, request bodies, and responses.

Publishing the fix does not remediate existing deployments. Before rollout, identify the package version and effective handlers in the deployed application, upgrade to 2.2.1 or later, and verify the deployment's endpoints with valid and invalid certificates. Network restrictions can reduce exposure but do not restore TLS peer authentication. Source inspection alone is not evidence that production uses the affected version or that exploitation has occurred.

## Package Logging Contract

Package-owned logs are designed not to emit:

- Bearer access tokens.
- `Authorization` header values.
- Client secrets.
- Cookies.
- Request payloads.
- Response bodies.
- OData key values in entity-key paths.
- OData filter/order/select/expand values in debug route logs.

A sanitized route keeps the origin/path shape and query-option names. For example, an internal request containing an entity ID and `$filter` value is logged in a shape similar to:

```text
https://contoso.api.crm5.dynamics.com/api/data/v9.2/rvl_egrheads(*)?$filter&$top
```

The value inside `(...)` and query-option values are omitted.

## Information-Level Logs

Information logs contain operational metadata such as:

- HTTP method.
- Entity name.
- HTTP status.
- Duration.
- Request/correlation ID.
- Transient flag.
- Mutation outcome.
- Retry number/delay/reason.
- Page/record counts.

Entity names and counts may still be sensitive in some deployments. Configure log access and retention to match the data classification.

## Framework HttpClient Logs

`IHttpClientFactory` normally emits Information logs containing full request URLs. Full OData URLs can reveal keys and filters.

For each named D365 registration, version 2 adds Warning-level filters for:

```text
System.Net.Http.HttpClient.D365Endpoint_<name>
System.Net.Http.HttpClient.D365Auth_<name>
```

This suppresses the standard Information-level logical/client handler URL logs that previously exposed full request routes.

Application logging configuration added later can override filters. Verify effective production logging and do not globally enable Information/Trace for `System.Net.Http.HttpClient.*` without checking output.

## Caller-Owned Sensitive Surfaces

The package returns diagnostic data that may contain sensitive information:

- `D365Response.RawBody`.
- `D365Response.Headers`.
- `D365Response.RequestUri`.
- `D365Exception.ResponseBody`.
- `D365Exception.RequestUri`.
- `D365Exception.D365ErrorMessage`.
- `InnerException` from networking/authentication libraries.

High-level HTTP exception bodies are capped at 64 KiB by default, but truncation is not redaction. Raw responses retain their complete buffered body.

Do not serialize response/exception objects directly into public API errors or telemetry.

## Safe Diagnostic Pattern

```csharp
catch (D365Exception exception)
{
    logger.LogWarning(
        "D365 failure kind={Kind}; status={Status}; entity={Entity}; request={RequestId}; transient={Transient}; outcome={Outcome}",
        exception.FailureKind,
        exception.StatusCode,
        exception.EntityName,
        exception.RequestId,
        exception.IsTransient,
        exception.MutationOutcome);
    throw;
}
```

Avoid logging `RequestUri`, response bodies, payloads, model objects, access tokens, or client configuration.

## Error Body Limits

The default high-level error body cap is 64 KiB. Advanced named options can reduce it:

```csharp
services.AddD365ODataClient("Sales", configuration, "D365:Sales");
services.Configure<D365ClientOptions>("Sales", options =>
{
    options.MaxErrorBodyBytes = 16 * 1024;
});
```

A value less than or equal to zero stores no response body in high-level HTTP exceptions. This does not affect raw `D365Response.RawBody` or successful response parsing.

## Secret Rotation

Immediately rotate a client secret if it appears in:

- Source control or commit history.
- Chat or ticket exports.
- Postman/environment exports shared outside the authorized boundary.
- Console/application logs.
- Crash dumps or telemetry.

Deleting the text is not sufficient after disclosure; treat it as compromised.

## Verification Checklist

1. Capture logs for successful GET/PATCH, 401, 400, 500, timeout, and retry.
2. Search for `Bearer`, known key values, filter values, client ID, client secret, and payload fields.
3. Confirm framework HttpClient Information logs are absent.
4. Confirm request IDs and statuses remain available.
5. Confirm application exception middleware does not serialize response bodies.
6. Confirm the deployed package includes the TLS fix, valid certificates succeed, and invalid certificates fail on both D365 API and ADFS authentication connections.
7. Confirm HTTP endpoints and foreign-origin request URLs fail before token acquisition/send, and 3xx responses are not followed automatically.
8. Run both target-framework test suites, the Windows/Linux CI matrix, and a dependency audit before release; confirm the consuming application loads the newly released package before an environment-specific smoke test.
