namespace FlintsLabs.D365.ODataClient.Transport;

internal static class D365LogSanitizer
{
    public static string Sanitize(Uri requestUri)
    {
        var builder = new UriBuilder(requestUri) { Query = string.Empty };
        return builder.Uri.ToString();
    }
}
