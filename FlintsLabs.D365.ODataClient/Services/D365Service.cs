using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Helpers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Main D365 OData service providing fluent query builder and CRUD operations
/// </summary>
public class D365Service : ID365Service
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<D365Service> _logger;
    private readonly ID365AccessTokenProvider _accessTokenProvider;
    private readonly D365ClientOptions _options;

    private string? AccessToken { get; set; }
    private string? _entity { get; set; }
    private string? _criteria { get; set; }
    private Dictionary<string, object?> _identities { get; set; } = [];

    private string _identity => string.Join(",",
        _identities.Select(a =>
            a.Value switch
            {
                Guid g => $"{a.Key}='{g}'",
                int or decimal or long or double => $"{a.Key}={a.Value}",
                _ => $"{a.Key}='{a.Value}'"
            }));

    private Dictionary<string, string> HeaderExtension { get; set; } = [];

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public D365Service(
        IHttpClientFactory httpClientFactory,
        ILogger<D365Service> logger,
        ID365AccessTokenProvider accessTokenProvider,
        IOptions<D365ClientOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _accessTokenProvider = accessTokenProvider;
        _options = options.Value;
    }

    private async Task<HttpRequestMessage> CreateHttpRequestMessageAsync(HttpMethod method, string url)
    {
        var token = await _accessTokenProvider.GetAccessTokenAsync();

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        // Add Extension headers
        foreach (var header in HeaderExtension)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    /// <inheritdoc />
    public D365Service Entity(string entity)
    {
        _entity = entity;
        _identities = [];
        return this;
    }

    /// <inheritdoc />
    public D365Service Entity(Enum entity)
    {
        return Entity(GetEntityNameFromEnum(entity));
    }

    /// <inheritdoc />
    public D365Query<T> Entity<T>(string entity)
    {
        return new D365Query<T>(_httpClientFactory, _logger, _accessTokenProvider, entity, _options);
    }

    /// <inheritdoc />
    public D365Query<T> Entity<T>(Enum entity)
    {
        return Entity<T>(GetEntityNameFromEnum(entity));
    }

    /// <summary>
    /// Extract entity name from user-defined enum.
    /// Priority: [Description] attribute > Enum name
    /// </summary>
    private static string GetEntityNameFromEnum(Enum entity)
    {
        var field = entity.GetType().GetField(entity.ToString());
        if (field == null)
            return entity.ToString();
        
        var descAttr = field.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        return descAttr?.Description ?? entity.ToString();
    }

    /// <summary>
    /// Add custom header to request
    /// </summary>
    public D365Service AddHeader(KeyValuePair<string, string> header)
    {
        HeaderExtension ??= [];
        if (!HeaderExtension.ContainsKey(header.Key))
        {
            HeaderExtension.Add(header.Key, header.Value);
        }

        return this;
    }

    /// <inheritdoc />
    public D365Service Where(string criteria)
    {
        _criteria = criteria;
        return this;
    }

    /// <inheritdoc />
    public D365Service Select(string[] selectColumns)
    {
        Dictionary<string, string> parameters = new()
        {
            ["$select"] = string.Join(',', selectColumns),
        };

        var query = QueryHelpers.AddQueryString("", parameters!);
        query = query[1..];
        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            _criteria += "&";
        }

        _criteria += query;

        return this;
    }

    /// <summary>
    /// Select specific properties using LINQ expression
    /// </summary>
    public D365Service Select<T>(Expression<Func<T, object>> expression)
    {
        string[] selectColumns = GetPropertyNamesFromExpression(expression);
        return Select(selectColumns);
    }

    /// <inheritdoc />
    public D365Service Expand(params string[] expandColumns)
    {
        if (expandColumns == null || expandColumns.Length == 0)
            return this;

        string expandValue = string.Join(',', expandColumns);

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            _criteria += "&";
        }

        _criteria += $"$expand={expandValue}";

        return this;
    }

    private static string[] GetPropertyNamesFromExpression<T>(Expression<Func<T, object>> expression)
    {
        var propertyNames = new List<string>();

        if (expression.Body is NewExpression newExpression)
        {
            foreach (var member in newExpression.Members!)
                propertyNames.Add(GetJsonPropertyNameOrDefault(member));
        }
        else if (expression.Body is MemberInitExpression memberInitExpression)
        {
            foreach (var binding in memberInitExpression.Bindings)
                propertyNames.Add(GetJsonPropertyNameOrDefault(binding.Member));
        }
        else
        {
            Expression body = expression.Body;
            if (body is UnaryExpression unaryExpression)
                body = unaryExpression.Operand;

            if (body is MemberExpression memberExpression)
                propertyNames.Add(GetJsonPropertyNameOrDefault(memberExpression.Member));
        }

        return propertyNames.ToArray();
    }

    private static string GetJsonPropertyNameOrDefault(MemberInfo member)
    {
        var jsonAttr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
        return jsonAttr?.Name ?? member.Name;
    }

    /// <inheritdoc />
    public D365Service AddIdentities(Dictionary<string, object> identities)
    {
        if (identities is null) throw new ArgumentNullException(nameof(identities));
        _identities = identities!;
        return this;
    }

    /// <inheritdoc />
    public D365Service AddIdentity(string property, string value)
    {
        _identities ??= [];
        _identities.Add(property, value);
        return this;
    }

    /// <inheritdoc />
    public D365Service AddIdentity(string property, object value)
    {
        _identities ??= [];
        _identities.Add(property, value);
        return this;
    }

    /// <summary>
    /// Add ordering to query
    /// </summary>
    public D365Service OrderBy(string criteria)
    {
        _criteria += criteria;
        return this;
    }

    /// <inheritdoc />
    public async Task<T?> FirstOrDefaultAsync<T>()
    {
        try
        {
            var content = await GetJsonData();
            var contentResult = JsonSerializer.Deserialize<List<T>>(content ?? "");
            return contentResult is null ? default : contentResult.FirstOrDefault();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in FirstOrDefaultAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<T>?> ToListAsync<T>()
    {
        try
        {
            var urlQuery = $"{_entity}/{_criteria}";
            _logger.LogInformation("Get: {Query}", urlQuery);

            var result = await GetListDataAsync<T>();
            return result;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ToListAsync");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<T1>?> ToListAsync<T, T1>(Func<T, T1> factory)
    {
        try
        {
            var urlQuery = $"{_entity}/{_criteria}";
            _logger.LogInformation("Get: {Query}", urlQuery);

            var result = await GetListDataAsync<T>();
            return result.Select(factory).ToList();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in ToListAsync with factory");
            throw;
        }
    }

    private async Task<string> GetJsonData()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var urlQuery = $"{_entity}{_criteria}";
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Get, urlQuery);
            var responseMessage = await httpClient.SendAsync(request);

            if (!responseMessage.IsSuccessStatusCode)
            {
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AccessToken = null;
                    return await GetJsonData();
                }
                else return string.Empty;
            }

            var responseContent = await responseMessage.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);

            var strContent = string.Empty;
            if (jsonResponse != null && jsonResponse.TryGetValue("value", out var accessTokenObj))
            {
                strContent = accessTokenObj.ToString()!;
            }

            return strContent;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in GetJsonData");
            return string.Empty;
        }
    }

    private async Task<List<T>> GetListDataAsync<T>()
    {
        var currentUrl = $"{_entity}/{_criteria}";
        _logger.LogInformation("GetListDataAsync starting with: {Url}", currentUrl);

        List<T> records = [];

        while (!string.IsNullOrEmpty(currentUrl))
        {
            var (nextUrl, responseString) = await GetResponseStringAsync(currentUrl);

            var record = DeserializeJsonContent<T>(responseString);
            records.AddRange(record);
            _logger.LogInformation("Fetched {Count} records", record.Count);

            currentUrl = nextUrl;
        }

        return records;
    }

    private async Task<(string NextUrl, string jsonResult)> GetResponseStringAsync(string url)
    {
        _logger.LogInformation("Fetching data from: {Url}", url);

        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
        var request = await CreateHttpRequestMessageAsync(HttpMethod.Get, url);
        var responseMessage = await httpClient.SendAsync(request);

        if (!responseMessage.IsSuccessStatusCode)
        {
            if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
            {
                AccessToken = null;
                return await GetResponseStringAsync(url);
            }

            _logger.LogError("Error fetching data: {StatusCode}", responseMessage.StatusCode);
            return (string.Empty, string.Empty);
        }

        var responseContent = await responseMessage.Content.ReadAsStringAsync();
        var nextLink = ExtractNextLinkFromContent(responseContent);

        return (nextLink, responseContent);
    }

    private static string ExtractNextLinkFromContent(string jsonContent)
    {
        try
        {
            using var document = JsonDocument.Parse(jsonContent);

            if (document.RootElement.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement))
            {
                return nextLinkElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static List<T> DeserializeJsonContent<T>(string jsonContent)
    {
        using var document = JsonDocument.Parse(jsonContent);

        if (document.RootElement.TryGetProperty("value", out JsonElement valueElement) &&
            valueElement.ValueKind == JsonValueKind.Array)
        {
            var list = valueElement.Deserialize<List<T>>();
            return list ?? [];
        }

        return [];
    }

    /// <inheritdoc />
    public async Task<T1?> AddAndParseObject<T, T1>(T obj)
    {
        try
        {
            var responseMessage = await PostJsonDataReturnMessage(obj);
            return await responseMessage.Content.ReadFromJsonAsync<T1>();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in AddAndParseObject");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> AddAsync<T>(T obj)
    {
        try
        {
            return await PostJsonData(obj);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in AddAsync");
            throw;
        }
    }

    private async Task<HttpResponseMessage> PostJsonDataReturnMessage<T>(T obj)
    {
        try
        {
            var jsonContent = JsonSerializer.Serialize(obj, JsonSerializerOptions);
            HttpContent content = new StringContent(jsonContent, Encoding.UTF8, MediaTypeNames.Application.Json);

            var urlQuery = $"{_entity}";
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Post, urlQuery);
            request.Content = content;

            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var responseMessage = await httpClient.SendAsync(request);

            return responseMessage;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in PostJsonDataReturnMessage");
            throw;
        }
    }

    private async Task<string> PostJsonData<T>(T obj)
    {
        try
        {
            var jsonContent = JsonSerializer.Serialize(obj, JsonSerializerOptions);
            HttpContent content = new StringContent(jsonContent, Encoding.UTF8, MediaTypeNames.Application.Json);

            var urlQuery = _entity ?? "";
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Post, urlQuery);
            request.Content = content;

            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var responseMessage = await httpClient.SendAsync(request);

            if (!responseMessage.IsSuccessStatusCode)
            {
                var strCon = await responseMessage.Content.ReadAsStringAsync();
                _logger.LogError("Post failed: {Response}", strCon);
                return strCon;
            }

            var responseContent = await responseMessage.Content.ReadAsStringAsync();
            _logger.LogInformation("Post successful");
            return responseContent;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in PostJsonData");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> DeleteAsync()
    {
        return await DeleteJsonData();
    }

    /// <inheritdoc />
    public async Task<string> UpdateAsync<T>(T obj)
    {
        try
        {
            return await PatchJsonData(obj);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in UpdateAsync");
            throw;
        }
    }

    private async Task<string> PatchJsonData<T>(T obj)
    {
        try
        {
            var urlQuery = $"{_entity}({_identity})/{_criteria}";

            var jsonContent = JsonSerializer.Serialize(obj, JsonSerializerOptions);
            HttpContent content = new StringContent(jsonContent, Encoding.UTF8, MediaTypeNames.Application.Json);
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Patch, urlQuery);
            request.Content = content;

            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var responseMessage = await httpClient.SendAsync(request);

            if (!responseMessage.IsSuccessStatusCode)
            {
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AccessToken = null;
                    return await PatchJsonData(obj);
                }
                
                var errorContent = await responseMessage.Content.ReadAsStringAsync();
                _logger.LogError("Patch failed: {Error}", errorContent);
                return errorContent;
            }
            
            if (responseMessage.StatusCode == HttpStatusCode.NoContent)
            {
                return string.Empty;
            }

            return await responseMessage.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in PatchJsonData");
            throw;
        }
    }

    private async Task<string> DeleteJsonData()
    {
        try
        {
            var urlQuery = $"{_entity}({_identity})/{_criteria}";
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Delete, urlQuery);
            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var responseMessage = await httpClient.SendAsync(request);

            if (!responseMessage.IsSuccessStatusCode)
            {
                if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
                {
                    AccessToken = null;
                    return await DeleteJsonData();
                }
                
                var errorContent = await responseMessage.Content.ReadAsStringAsync();
                _logger.LogError("Delete failed: {Error}", errorContent);
                return errorContent;
            }
            
            if (responseMessage.StatusCode == HttpStatusCode.NoContent)
            {
                return string.Empty;
            }

            var responseContent = await responseMessage.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);

            var strContent = string.Empty;
            if (jsonResponse != null && jsonResponse.TryGetValue("value", out var accessTokenObj))
            {
                strContent = accessTokenObj.ToString()!;
            }

            return strContent;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error in DeleteJsonData");
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{_entity}{_criteria}";
    }

    /// <inheritdoc />
    public (bool IsError, string Message) TryParseD365ErrorMessage(string jsonMessage)
    {
        if (string.IsNullOrWhiteSpace(jsonMessage))
        {
            return (false, "Input JSON message is null or empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonMessage);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("innererror", out var innerErrorElement))
                {
                    if (innerErrorElement.TryGetProperty("message", out var innerMessageElement) &&
                        innerMessageElement.ValueKind == JsonValueKind.String)
                    {
                        return (true, innerMessageElement.GetString() ?? string.Empty);
                    }
                }

                if (errorElement.TryGetProperty("message", out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                {
                    return (false, messageElement.GetString() ?? string.Empty);
                }
            }

            return (false, "Could not find a valid 'error' property in the JSON response.");
        }
        catch (JsonException ex)
        {
            return (false, $"Failed to parse JSON: {ex.Message}");
        }
    }
}
