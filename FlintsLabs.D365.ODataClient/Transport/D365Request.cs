using System.Net.Http.Headers;
using System.Text;

namespace FlintsLabs.D365.ODataClient.Transport;

internal sealed record D365Request(
    HttpMethod Method,
    string RelativeOrAbsoluteUrl,
    string? JsonPayload,
    string? EntityName,
    IReadOnlyDictionary<string, string> Headers)
{
    public bool IsMutation => Method == HttpMethod.Post
                              || Method == HttpMethod.Patch
                              || Method == HttpMethod.Delete;

    public static D365Request Get(string url, string? entityName)
    {
        return new D365Request(
            HttpMethod.Get,
            url,
            null,
            entityName,
            new Dictionary<string, string>());
    }

    public static D365Request Json(
        HttpMethod method,
        string url,
        string jsonPayload,
        string? entityName,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        return new D365Request(
            method,
            url,
            jsonPayload,
            entityName,
            headers ?? new Dictionary<string, string>());
    }

    public HttpRequestMessage CreateMessage(string accessToken)
    {
        var request = new HttpRequestMessage(Method, RelativeOrAbsoluteUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        foreach (var header in Headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (JsonPayload is not null)
            request.Content = new StringContent(JsonPayload, Encoding.UTF8, "application/json");

        return request;
    }
}
