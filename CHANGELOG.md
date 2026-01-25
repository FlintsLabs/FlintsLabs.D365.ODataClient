# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
