# Managed Identity Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add strict configuration-driven Managed Identity authentication and publish package version 2.2.0.

**Architecture:** `FromConfiguration()` resolves an explicit named `D365AuthType` before validation and otherwise retains legacy ADFS detection. Authentication-specific validation ensures Managed Identity never requires or falls back to client-secret credentials.

**Tech Stack:** C# 13, .NET 8/.NET 10, Microsoft.Extensions.Configuration, xUnit, MSAL 4.67.2.

## Global Constraints

- Explicit `AuthType` accepts only `AzureAD`, `ADFS`, and `ManagedIdentity` names, case-insensitively.
- Managed Identity never falls back to Azure AD client-secret authentication.
- Missing `ManagedIdentityClientId` selects System-assigned Managed Identity; a supplied value must be a GUID.
- Existing configurations without `AuthType` preserve Azure AD/ADFS behavior.
- Do not add dependencies or expose credential values in messages/logs.

---

### Task 1: Configuration parsing and validation

**Files:**
- Modify: `FlintsLabs.D365.ODataClient/Extensions/D365ClientOptions.cs`
- Test: `FlintsLabs.D365.ODataClient.Tests/UnitTests/ConfigurationTests.cs`

**Interfaces:**
- Consumes: `D365ClientBuilder.FromConfiguration(IConfiguration, string)` and `D365AuthType`.
- Produces: strict `AuthType` parsing and authentication-specific validation using existing `D365ClientOptions` properties.

- [ ] Write failing tests for User/System-assigned Managed Identity configuration, missing secret fields, invalid auth type/GUID/token target, explicit Azure AD/ADFS, and legacy detection.
- [ ] Run targeted `ConfigurationTests` on .NET 8 and confirm failures are caused by unsupported configuration fields.
- [ ] Parse explicit named `AuthType` before validation; bind and validate `ManagedIdentityClientId`; preserve legacy detection when absent.
- [ ] Run targeted tests and refactor validation without changing fluent selector behavior.

### Task 2: Version and documentation

**Files:**
- Modify: `FlintsLabs.D365.ODataClient/FlintsLabs.D365.ODataClient.csproj`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/v2/authentication-and-parallelism.md`
- Modify: `docs/v2/getting-started.md`
- Modify: `docs/v2/README.md`

**Interfaces:**
- Consumes: the configuration contract delivered by Task 1.
- Produces: package metadata and examples for version 2.2.0.

- [ ] Add System/User-assigned JSON examples and state explicit precedence/no-fallback behavior.
- [ ] Add the 2.2.0 changelog entry and update package/readme versions and release notes.
- [ ] Search documentation for stale statements that Managed Identity is fluent-only.

### Task 3: Regression and release

**Files:**
- Verify: all modified files and generated `FlintsLabs.D365.ODataClient.2.2.0.nupkg`.

**Interfaces:**
- Consumes: green implementation and release documentation.
- Produces: merged/pushed `main`, tag `v2.2.0`, GitHub Release, and public NuGet 2.2.0 package.

- [ ] Run non-integration tests for .NET 8 and .NET 10.
- [ ] Build the compiled sample in Release configuration.
- [ ] Pack locally and inspect package version, repository commit, dependencies, and contents.
- [ ] Commit, merge into `main`, rerun verification, push `main`, tag `v2.2.0`, and monitor the publish workflow.
- [ ] Confirm GitHub Release and NuGet public index/download availability.
