# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.0] - 2026-08-08
### Added
- Strict `FromConfiguration()` support for `AuthType: ManagedIdentity` and optional `ManagedIdentityClientId`
- Configuration-driven System-assigned Managed Identity when `ManagedIdentityClientId` is omitted

### Changed
- Managed Identity configuration no longer requires or retains `ClientId`, `ClientSecret`, or `TenantId`
- Explicit named `AuthType` values take precedence over legacy ADFS auto-detection; invalid, empty, or numeric values fail without fallback
- Configurations without `AuthType` preserve the existing Azure AD/ADFS detection behavior

## [2.1.0] - 2026-08-08
### Added
- Fluent System-assigned and User-assigned Azure Managed Identity authentication using the existing MSAL dependency
- Typed `D365TokenAcquisitionException` for MSAL acquisition failures without credential data in the package message

### Changed
- Managed Identity token acquisition shares one MSAL application per named client and forces one refresh only after D365 rejects a token with HTTP 401
- `FromConfiguration()` remains limited to Azure AD client-secret and ADFS authentication; Managed Identity requires an explicit fluent selector and never falls back to a client secret

## [2.0.0] - 2026-07-17
### Breaking
- Replaced `ID365Service` / `ID365ServiceFactory` with `ID365Client` / `ID365ClientFactory`
- Removed the version 1 compatibility service instead of retaining default-return behavior
- Require generic `Entity<T>(...)` query creation through `ID365Client`
- High-level reads now throw for HTTP, authentication, transport, timeout, protocol, and serialization failures instead of returning `null`, empty lists, or zero
- High-level mutations now return `D365Response` and throw for every non-2xx status, including DELETE 404
- Strict count parsing now requires a valid non-negative 64-bit `@odata.count`; `CountAsync` throws on `int` overflow

### Added
- Fail-closed typed exception model with failure kind, HTTP/D365 details, request ID, retry guidance, mutation outcome, and partial page count
- Raw `ID365Client.SendAsync` API that preserves every received HTTP status
- `D365Response<T>` for typed mutation responses
- `LongCountAsync` and strict collection-envelope validation
- Validated same-endpoint pagination, loop detection, configurable `MaxPages`, and all-or-error list results
- Opt-in bounded retries for GET/HEAD with `Retry-After`, exponential backoff, and jitter
- Per-request timeout and cancellation propagation across token acquisition, transport, retry delay, and pagination
- Named singleton clients with shared token cache and single-flight acquisition/refresh for parallel callers
- Safe OData literal formatting and URI encoding for GUIDs, dates, numerics, apostrophes, reserved characters, and Unicode
- Official version 2 migration, operations, security, query, authentication, and BioWMS recovery documentation

### Changed
- Refresh and resend at most once after an actual HTTP 401; mutation retries remain disabled for ambiguous timeout/transport/408/429/5xx outcomes
- Typed POST requires a non-empty non-null JSON representation and preserves `SucceededOrAccepted` when response parsing fails
- Framework `HttpClient` Information logs for package-named clients are filtered to prevent full OData URLs from exposing key/filter values
- Package-owned logs no longer include bearer tokens, payloads, response bodies, key values, or query-option values
- Target frameworks remain .NET 8 and .NET 10

### Security
- Known compatibility risk: version 2.0.0 retains the existing permissive TLS certificate callback for D365 and authentication clients. It accepts every server certificate. Deploy only on a trusted network path and review `docs/v2/security-and-logging.md`.
- Application code remains responsible for redacting raw response/exception bodies, headers, and request URIs before logging.

See [Migration from version 1](docs/v2/migration-from-v1.md) for required application changes.

## [1.2.27] - 2026-02-09
### Added
- LINQ null-coalescing (`??`) translation support to OData `coalesce(left,right)`
- Clear graceful error when coalesce translation is unsupported
- Unit tests for coalesce translator and query URL output

### Changed
- Mask `Authorization` header value in request logs
- Reduced noisy request logs from `Information` to `Debug` in key paths
- Share token provider across service instances with lock-protected token refresh for parallel calls

## [1.2.26] - 2026-02-04
### Added
- `[OdataKey]` attribute for key-based Update/Delete via `Where(...)`
- Supports composite keys when multiple `[OdataKey]` are present
- README examples for `[OdataKey]` Update/Delete

## [1.2.25] - 2026-01-26
### Fixed
- Fixed `BooleanFormatting` configuration being ignored in `appsettings.json`

## [1.2.24] - 2026-01-26
### Fixed
- Issue where `!x.Prop.GetValueOrDefault()` generated invalid filter (`$filter=null`)
- Correctly translates `GetValueOrDefault()` to `(Prop eq true)`
- Correctly handles Unary `Not` expression (`!`)

## [1.2.23] - 2026-01-26
### Added
- Configurable Boolean Formatting: `NoYes` (default) or `Literal` (true/false)
- Support for Dataverse/CRM boolean filtering style via `WithBooleanFormatting(D365BooleanFormatting.Literal)`

## [1.2.22] - 2026-01-26
### Added
- `Expand(string)` and `Expand(Expression)` overloads for simple extensions
- `AddHeader(key, value)` method for custom headers (e.g. `Prefer`)
- Request logging (Method, URL, Headers)

### Fixed
- Issue where `Expand(x => x)` threw NotSupportedException (now implies `select=*`)

## [1.2.21] - 2026-01-25
### Added
- LINQ Expression support for OrderBy: `.OrderBy(x => x.Property)`
- `OrderByDescending<TKey>()` method for descending sort
- `ThenBy<TKey>()` and `ThenByDescending<TKey>()` for multi-level sorting
- Respects `[JsonPropertyName]` attribute for property name resolution

## [1.2.20] - 2026-01-09
### Changed
- Cached `JsonSerializerOptions` as static readonly (replaces 5 inline allocations)
- Reduces memory allocations during POST/PATCH serialization

## [1.2.19] - 2026-01-09
### Changed
- Thread-safe registration using `ConcurrentDictionary` with `TryAdd`
- Prevents race conditions during parallel service registration

## [1.2.18] - 2026-01-08
### Added
- ConcurrentDictionary cache for enum entity name lookups (performance optimization)
- First call uses reflection, subsequent calls are O(1) dictionary lookups

## [1.2.17] - 2026-01-08
### Added
- Startup validation for required configuration fields (ClientId, ClientSecret, Resource/OrganizationUrl, TenantId)
- Throws `InvalidOperationException` at startup if config is missing (fail fast)

## [1.2.16] - 2026-01-08
### Added
- `Entity(Enum)` overloads for user-defined type-safe entity names
- Uses `[Description]` attribute for entity name mapping
- Fallback to enum member name if no description

## [1.2.15] - 2026-01-08
### Added
- Duplicate registration check to prevent silent overwrites
- Throws `InvalidOperationException` if same client name is registered twice

## [1.2.14] - 2026-01-08
### Added
- `List<T>.Contains()` support for IN clause (auto-generates OR filters)
- Works with `List`, `Array`, and `IEnumerable`
- Example: `.Where(x => codes.Contains(x.ItemNumber))`

## [1.2.13] - 2026-01-08
### Fixed
- StringBuilder to string conversion in logging methods
### Added
- Logging section in README

## [1.2.12] - 2026-01-08
### Added
- Request body logging for POST/PATCH methods (LogDebug level)

## [1.2.11] - 2026-01-08
### Added
- Enhanced logging with full absolute URLs for all HTTP requests
- `GetFullUrl()` helper method for consistent URL logging

## [1.2.10] - 2026-01-08
### Added
- Official .NET 10 support (multi-targeting net8.0;net10.0)
- .NET 10.0 badge in README
- Verification (.NET 10) section in README

## [1.2.9] - 2026-01-07
### Added
- Development section in README with test instructions

## [1.2.8] - 2026-01-07
### Added
- xUnit test project with unit and integration tests
- Secured configuration with `.gitignore` (appsettings.json excluded)
- `appsettings.example.json` files as templates

## [1.2.7] - 2026-01-06
### Added
- Table of Contents to README

## [1.2.6] - 2026-01-06
### Improved
- README with step-by-step controller examples

## [1.2.5] - 2026-01-06
### Fixed
- HttpClient naming for multi-source scenarios

## [1.2.4] - 2026-01-06
### Changed
- Updated README on NuGet.org

## [1.2.3] - 2026-01-06
### Added
- Support for Microsoft Dataverse (CRM / Power Platform)
- Improved auth logic for different D365 environments

## [1.2.0] - 2026-01-05
### Added
- Multi-source support with `ID365ServiceFactory`
- Fluent builder pattern for configuration
- Support for multiple D365 instances (Cloud + OnPrem)

## [1.1.0] - 2026-01-05
### Added
- Unified token provider supporting both Azure AD and ADFS
- ADFS authentication support for On-Premise D365

## [1.0.1] - 2026-01-04
### Added
- PackageProjectUrl for NuGet-GitHub linking

## [1.0.0] - 2026-01-04
### Added
- Initial release
- Fluent API for D365 OData queries
- Token management (Azure AD)
- Query builder with `Where`, `Select`, `Expand`, `Take`
- CRUD operations (AddAsync, UpdateAsync, DeleteAsync)
- Cross-company queries support
