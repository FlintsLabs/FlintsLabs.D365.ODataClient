using FlintsLabs.D365.ODataClient.Exceptions;

namespace FlintsLabs.D365.ODataClient.OData;

internal sealed class ODataNextLinkValidator
{
    private readonly Uri _baseUri;

    public ODataNextLinkValidator(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || !IsHttpScheme(baseUri.Scheme))
        {
            throw new D365ProtocolException(
                "The configured D365 API base URL must be an absolute HTTP or HTTPS URL.");
        }

        var normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri(baseUri.AbsoluteUri + "/", UriKind.Absolute);
        _baseUri = normalized;
    }

    public Uri Resolve(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            throw new D365ProtocolException("The D365 pagination link is empty.");

        Uri candidate;
        try
        {
            if (HasExplicitScheme(link))
            {
                if (!Uri.TryCreate(link, UriKind.Absolute, out var absolute))
                    throw new UriFormatException("Invalid absolute pagination URI.");
                candidate = absolute;
            }
            else
            {
                candidate = new Uri(_baseUri, link);
            }
        }
        catch (UriFormatException exception)
        {
            throw new D365ProtocolException(
                "The D365 pagination link is not a valid URI.",
                innerException: exception);
        }

        if (!IsHttpScheme(candidate.Scheme))
            throw new D365ProtocolException("The D365 pagination link must use HTTP or HTTPS.");
        if (!string.IsNullOrEmpty(candidate.UserInfo))
            throw new D365ProtocolException("The D365 pagination link must not contain user information.");
        if (!string.IsNullOrEmpty(candidate.Fragment))
            throw new D365ProtocolException("The D365 pagination link must not contain a URI fragment.");
        if (!string.Equals(candidate.Scheme, _baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(candidate.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || candidate.Port != _baseUri.Port)
        {
            throw new D365ProtocolException(
                "The D365 pagination link points to a different endpoint.");
        }

        if (!_baseUri.IsBaseOf(candidate))
        {
            throw new D365ProtocolException(
                "The D365 pagination link is outside the configured API base path.");
        }

        return candidate;
    }

    private static bool IsHttpScheme(string scheme)
    {
        return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitScheme(string value)
    {
        var colonIndex = value.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        for (var index = 0; index < colonIndex; index++)
        {
            var character = value[index];
            if (index == 0 && !char.IsAsciiLetter(character))
                return false;
            if (index > 0
                && !char.IsAsciiLetterOrDigit(character)
                && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
