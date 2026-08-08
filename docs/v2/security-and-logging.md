# Security and Logging

This guide defines what the package redacts, what remains the caller's responsibility, and the known TLS risk in version 2.x.

## Critical TLS Caveat

Version 2.x registers both the D365 API and authentication `HttpClient` handlers with a certificate-validation callback that returns `true` for every server certificate.

Consequences:

- Certificate-chain validation is bypassed.
- Hostname validation is bypassed.
- Expired, self-signed, or attacker-provided certificates are accepted.
- HTTPS encryption alone does not prove the identity of D365, Azure AD, or ADFS.
- An active attacker on the network path could intercept access tokens, client credentials, request bodies, and D365 responses.

This behavior is intentionally preserved from version 1 for version 2 compatibility; it is not a secure default and is not a recommendation.

Operational requirements while using version 2.x:

1. Use only a trusted and controlled network path.
2. Restrict application egress to the expected D365 and authority endpoints.
3. Keep secrets in a managed secret store and rotate exposed credentials.
4. Do not rely on the package's TLS connection for peer-authentication assurance.
5. Record this exception in the deployment's security risk register.
6. Plan a package upgrade/remediation that restores normal certificate validation.

If certificate validation is mandatory for the environment, do not deploy version 2.x without an independently reviewed transport remediation. The public builder currently does not expose a certificate-policy switch.

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
6. Confirm egress restrictions and the accepted TLS risk are documented.
