# FlintsLabs.D365.ODataClient 2.0 Documentation

Version 2 changes the package's reliability contract: a high-level call either returns a successful, validated result or throws an exception that preserves the failure signal. It does not convert an unavailable D365 endpoint into a business-level "not found" result.

## Start Here

| Guide | Use it for |
| --- | --- |
| [Getting started](getting-started.md) | Installation, registration, models, reads, and mutations |
| [Migration from version 1](migration-from-v1.md) | Breaking API and behavior changes |
| [Error handling](error-handling.md) | Exception taxonomy, properties, and fail-closed reads |
| [Mutations and responses](mutations-and-responses.md) | Raw/ensured behavior and mutation outcomes |
| [Retry, timeout, and cancellation](retry-timeout-cancellation.md) | Read retries and ambiguous writes |
| [Authentication and parallelism](authentication-and-parallelism.md) | Azure AD, ADFS, named clients, token sharing |
| [Queries and pagination](query-and-pagination.md) | LINQ translation, keys, counts, and next links |
| [Security and logging](security-and-logging.md) | TLS caveat, redaction, and safe diagnostics |
| [BioWMS recovery pattern](biowms-recovery-pattern.md) | Exact preflight and reconciliation workflow |

The runnable source examples are in [the v2 sample project](../../samples/FlintsLabs.D365.ODataClient.V2.Examples/Program.cs).

## Core Contract

- `FirstOrDefaultAsync()` returns `null` only after a successful, valid collection response with an empty `value` array.
- `ToListAsync()` returns an empty list only after a successful, valid empty query.
- High-level HTTP 4xx/5xx, authentication, transport, timeout, protocol, and serialization failures throw.
- High-level POST/PATCH/DELETE throw for non-2xx responses.
- Raw `ID365Client.SendAsync()` returns received HTTP statuses and leaves status interpretation to the caller.
- A mutation with an unknown outcome must be reconciled by exact key before a caller-controlled retry.
- Pagination is all-or-error; partial records are never returned as if the query succeeded.

## Supported Targets

The 2.0.0 package targets `net8.0` and `net10.0`.

## Security Boundary

Version 2.0.0 intentionally preserves the previous permissive server-certificate callback for compatibility. It accepts all server certificates for D365 and authentication requests. Read [Security and logging](security-and-logging.md) and deploy only across a trusted network path until this behavior is remediated.
