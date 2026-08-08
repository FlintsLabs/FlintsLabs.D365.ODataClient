# Managed Identity Configuration Design

## Goal

Allow version 2.2.0 applications to select Azure Managed Identity through `FromConfiguration()` without retaining a client secret, while preserving the existing fluent selectors and legacy Azure AD/ADFS configuration behavior.

## Configuration Contract

`AuthType` is optional. When present, it accepts the named `D365AuthType` values `AzureAD`, `ADFS`, or `ManagedIdentity`, case-insensitively. Empty, numeric, and unknown values fail immediately; there is no fallback. When absent, the existing ADFS auto-detection remains unchanged.

For `ManagedIdentity`, `ManagedIdentityClientId` is optional. Missing or whitespace selects System-assigned Managed Identity. A supplied value must be a GUID and selects User-assigned Managed Identity. `ClientId`, `ClientSecret`, and `TenantId` are not required and are cleared from the resulting options. Token acquisition still requires explicit `Scope` or `Resource`; endpoint construction still requires `Resource` or `OrganizationUrl`.

Azure AD and ADFS keep their existing client credential validation. Fluent selectors continue to use the last selector called.

## Failure Behavior

Invalid `AuthType`, invalid Managed Identity client ID, or missing Managed Identity token target throws `InvalidOperationException` during service registration. Messages identify configuration field names but never include IDs, secrets, or tokens.

## Verification

Tests cover User-assigned and System-assigned configuration, missing secret fields, strict authentication type parsing, invalid GUIDs, missing token targets, explicit Azure AD/ADFS, and legacy auto-detection. Release verification runs non-integration tests for .NET 8 and .NET 10 plus the compiled sample build.
