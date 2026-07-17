namespace FlintsLabs.D365.ODataClient.Transport;

internal static class D365LogSanitizer
{
    public static string Sanitize(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);

        var authority = new UriBuilder(requestUri.Scheme, requestUri.IdnHost, requestUri.Port).Uri
            .GetLeftPart(UriPartial.Authority);
        var path = RedactKeyValues(requestUri.AbsolutePath);
        var optionNames = requestUri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Contains('='))
            .Select(part => part.Split('=', 2)[0])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Uri.UnescapeDataString);
        var queryShape = string.Join('&', optionNames);

        return string.IsNullOrEmpty(queryShape)
            ? $"{authority}{path}"
            : $"{authority}{path}?{queryShape}";
    }

    private static string RedactKeyValues(string path)
    {
        path = path
            .Replace("%28", "(", StringComparison.OrdinalIgnoreCase)
            .Replace("%29", ")", StringComparison.OrdinalIgnoreCase);
        var result = new System.Text.StringBuilder(path.Length);
        var depth = 0;
        foreach (var character in path)
        {
            if (character == '(')
            {
                if (depth == 0)
                    result.Append("(*)");
                depth++;
                continue;
            }

            if (character == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0)
                result.Append(character);
        }

        return result.ToString();
    }
}
