using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Transport;

internal static class D365EndpointPolicy
{
    public static void ValidateConfiguration(D365ClientOptions options)
    {
        GetApiBaseUri(options);
        if (options.AuthType == D365AuthType.ADFS)
            GetAdfsTokenUri(options);
    }

    public static Uri GetApiBaseUri(D365ClientOptions options)
    {
        // With OrganizationUrl, Resource is an OAuth audience, not a network destination
        // (for example an ADFS urn). Only the effective API endpoint must be HTTPS.
        var endpoint = !string.IsNullOrWhiteSpace(options.OrganizationUrl)
            ? options.OrganizationUrl
            : options.Resource;
        GetHttpsUri(endpoint, "API endpoint", allowQuery: false);
        return GetHttpsUri(options.GetBaseUrl(), "API base URL", allowQuery: false);
    }

    public static Uri GetAdfsTokenUri(D365ClientOptions options) =>
        GetHttpsUri(options.TokenEndpoint, "ADFS TokenEndpoint", allowQuery: true);

    public static Uri ResolveRequestUri(D365ClientOptions options, D365Request request)
    {
        var baseUri = GetApiBaseUri(options);
        // Resolve once using the configured origin, not a customizable HttpClient.BaseAddress.
        // This also handles root-relative and network-path references like HttpClient does.
        if (!Uri.TryCreate(baseUri, request.RelativeOrAbsoluteUrl, out var candidate)
            || !IsSecureEndpoint(candidate)
            || !string.Equals(candidate.IdnHost, baseUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || candidate.Port != baseUri.Port
            || request.Headers.Keys.Any(key => string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase)))
        {
            // Do not include the rejected URI or parser exceptions: they may contain credentials.
            throw new D365ProtocolException(
                "D365 requests must use HTTPS on the configured API origin, without user information, fragments, or a Host header override.",
                method: request.Method,
                entityName: request.EntityName,
                mutationOutcome: request.IsMutation ? D365MutationOutcome.NotSent : D365MutationOutcome.NotApplicable);
        }

        return candidate;
    }

    private static Uri GetHttpsUri(string? value, string field, bool allowQuery)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !IsSecureEndpoint(uri)
            || (!allowQuery && !string.IsNullOrEmpty(uri.Query)))
        {
            throw new InvalidOperationException(
                $"D365 {field} must be an absolute HTTPS URL without user information or fragments"
                + (allowQuery ? "." : " or a query string."));
        }

        return uri;
    }

    private static bool IsSecureEndpoint(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Fragment);
}
