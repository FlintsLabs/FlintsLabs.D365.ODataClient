using System.Text.Json;

namespace FlintsLabs.D365.ODataClient.Transport;

internal sealed record D365ErrorDetails(string? Code, string? Message);

internal static class D365ErrorParser
{
    public static D365ErrorDetails Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new D365ErrorDetails(null, null);

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return new D365ErrorDetails(null, null);
            }

            var code = ReadString(error, "code");
            var message = ReadString(error, "message");
            return new D365ErrorDetails(code, message);
        }
        catch (JsonException)
        {
            return new D365ErrorDetails(null, null);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }
}
