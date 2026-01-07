using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Expressions;
using FlintsLabs.D365.ODataClient.Extensions;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Services;

/// <summary>
/// Generic query builder for D365 entities with fluent API and LINQ support
/// </summary>
public class D365Query<T>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly ID365AccessTokenProvider _tokenProvider;
    private readonly D365ClientOptions _options;
    private readonly string _entity;
    private string _criteria = string.Empty;
    private bool _crossCompany;
    private readonly Dictionary<string, string> _headerExtension = new();

    // Identity (used for PATCH / DELETE)
    private readonly Dictionary<string, object?> _identities = new();

    // Client-side filtering & paging
    private Expression<Func<T, bool>>? _clientPredicate;
    private int? _takeCount;

    public D365Query(
        IHttpClientFactory factory,
        ILogger logger,
        ID365AccessTokenProvider tokenProvider,
        string entity,
        D365ClientOptions options)
    {
        _httpClientFactory = factory;
        _logger = logger;
        _tokenProvider = tokenProvider;
        _entity = entity;
        _options = options;
    }

    #region Header & Utility

    /// <summary>
    /// Add custom header to request
    /// </summary>
    public D365Query<T> AddHeader(string key, string value)
    {
        _headerExtension.TryAdd(key, value);
        return this;
    }

    /// <summary>
    /// Set page size for OData pagination using "Prefer: odata.maxpagesize" header
    /// </summary>
    /// <param name="size">Number of records per page (recommended 100-500)</param>
    public D365Query<T> PageSize(int size)
    {
        return AddHeader("Prefer", $"odata.maxpagesize={size}");
    }

    /// <summary>
    /// Add entity key identity for single-record operations (PATCH/DELETE)
    /// </summary>
    public D365Query<T> AddIdentity(string key, object? value)
    {
        _identities.TryAdd(key, value);
        return this;
    }

    private async Task<HttpRequestMessage> CreateHttpRequestMessageAsync(HttpMethod method, string url)
    {
        var token = await _tokenProvider.GetAccessTokenAsync();

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        // Add extension headers
        foreach (var header in _headerExtension)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    /// <summary>
    /// Get full absolute URL for logging
    /// </summary>
    private string GetFullUrl(string relativeUrl)
    {
        return $"{_options.GetBaseUrl()}{relativeUrl}";
    }

    #endregion

    /// <summary>
    /// Enable cross-company query
    /// </summary>
    public D365Query<T> CrossCompany(bool enable = true)
    {
        _crossCompany = enable;
        return this;
    }

    /// <summary>
    /// Enable $count in response
    /// </summary>
    public D365Query<T> Count(bool enable = true)
    {
        AppendCriteria("$count=" + (enable ? "true" : "false"));
        return this;
    }

    /// <summary>
    /// Add OData filter using LINQ expression
    /// </summary>
    public D365Query<T> Where(Expression<Func<T, bool>> predicate)
    {
        var visitor = new D365ExpressionVisitor();
        var filter = visitor.Translate(predicate.Body);
        AppendCriteria($"$filter={filter}");
        return this;
    }

    /// <summary>
    /// Select specific properties using LINQ expression
    /// </summary>
    public D365Query<T> Select(Expression<Func<T, object>> selector)
    {
        var props = D365ExpressionHelper.GetPropertyNamesFromExpression(typeof(T), selector);
        AppendCriteria($"$select={string.Join(',', props)}");
        return this;
    }

    /// <summary>
    /// Select specific properties by name
    /// </summary>
    public D365Query<T> Select(string[] selectColumns)
    {
        if (selectColumns == null || selectColumns.Length == 0)
            return this;

        var queryString = string.Join(",", selectColumns);
        AppendCriteria($"$select={queryString}");
        return this;
    }

    /// <summary>
    /// Skip N records
    /// </summary>
    public D365Query<T> Skip(int count)
    {
        AppendCriteria($"$skip={count}");
        return this;
    }

    /// <summary>
    /// Take N records (client-side or server-side depending on WhereClient usage)
    /// </summary>
    public D365Query<T> Take(int count)
    {
        _takeCount = count;
        return this;
    }

    /// <summary>
    /// Apply client-side filtering (evaluated after server response)
    /// </summary>
    public D365Query<T> WhereClient(Expression<Func<T, bool>> predicate)
    {
        _clientPredicate = predicate;
        return this;
    }

    /// <summary>
    /// Order by property
    /// </summary>
    /// <param name="sortLabel">Property name to sort by</param>
    /// <param name="sortDirection">true = ascending, false = descending</param>
    public D365Query<T> OrderBy(string sortLabel, bool sortDirection)
    {
        var direction = sortDirection ? "asc" : "desc";
        AppendCriteria($"$orderby={sortLabel} {direction}");
        return this;
    }

    /// <summary>
    /// Expand navigation property with select
    /// </summary>
    public D365Query<T> Expand<TExpand>(
        Expression<Func<T, object>> navigation,
        Expression<Func<TExpand, object>> select)
    {
        var navName = D365ExpressionHelper.GetPropertyName(navigation);
        var selectCols = D365ExpressionHelper.GetPropertyNamesFromExpression(typeof(TExpand), select);
        AppendCriteria($"$expand={navName}($select={string.Join(',', selectCols)})");
        return this;
    }

    /// <summary>
    /// Expand navigation property by name with select
    /// </summary>
    public D365Query<T> Expand<TExpand>(
        string navigationName,
        Expression<Func<TExpand, object>> select)
    {
        var selectCols = D365ExpressionHelper.GetPropertyNamesFromExpression(typeof(TExpand), select);
        AppendCriteria($"$expand={navigationName}($select={string.Join(',', selectCols)})");
        return this;
    }

    /// <summary>
    /// Execute query and return first record or default
    /// </summary>
    public async Task<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        Take(1);
        var list = await ToListAsync(cancellationToken);
        return list.FirstOrDefault();
    }

    /// <summary>
    /// Execute query and return all matching records
    /// </summary>
    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (_crossCompany)
            queryParts.Add("cross-company=true");
        if (!string.IsNullOrWhiteSpace(_criteria))
            queryParts.Add(_criteria);

        // If no client predicate, send $top to server
        if (_clientPredicate == null && _takeCount.HasValue)
        {
            queryParts.Add($"$top={_takeCount}");
        }

        var baseUrl = $"{_entity}?{string.Join("&", queryParts)}";
        _logger.LogInformation("D365 GET: {Url}", GetFullUrl(baseUrl));

        var records = new List<T>();
        var currentUrl = baseUrl;

        // Fetch data continuously until nextLink is exhausted
        while (!string.IsNullOrEmpty(currentUrl))
        {
            // If limited and already have enough (for client filter case), stop
            if (_clientPredicate != null && _takeCount.HasValue && records.Count >= _takeCount.Value)
                break;

            var (nextUrl, jsonDocument, _) = await GetResponseJsonDocumentAsync(currentUrl, cancellationToken);

            if (jsonDocument is null)
                break;

            using var doc = jsonDocument;
            if (doc.RootElement.TryGetProperty("value", out JsonElement valueElement) &&
                valueElement.ValueKind == JsonValueKind.Array)
            {
                if (_clientPredicate != null)
                {
                    // Scan JSON elements first without deserializing everything
                    foreach (var element in valueElement.EnumerateArray())
                    {
                        var evaluator = new JsonElementExpressionEvaluator(element);
                        var isMatch = evaluator.Evaluate(_clientPredicate);

                        if (isMatch)
                        {
                            var item = element.Deserialize<T>();
                            if (item != null)
                            {
                                records.Add(item);
                                if (_takeCount.HasValue && records.Count >= _takeCount.Value)
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    // No client predicate -> deserialize all
                    var chunk = valueElement.Deserialize<List<T>>() ?? [];
                    records.AddRange(chunk);
                }
            }

            _logger.LogInformation("Fetched chunk (total collected {Count})", records.Count);
            currentUrl = nextUrl;
        }

        _logger.LogInformation("All pages fetched: {Count} records total", records.Count);
        return records;
    }

    /// <summary>
    /// Create a new record (POST)
    /// </summary>
    public async Task<string> AddAsync(T obj)
    {
        var url = $"{_entity}";
        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url));

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);

        var request = await CreateHttpRequestMessageAsync(HttpMethod.Post, url);
        request.Content = content;

        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("POST failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogInformation("Record created successfully.");
        return result;
    }

    /// <summary>
    /// Create a new record with anonymous object (POST)
    /// </summary>
    public async Task<string> AddAsync(object obj)
    {
        var url = new StringBuilder(_entity);

        // Support Cross-company if enabled
        var hasQuery = false;
        if (_crossCompany)
        {
            url.Append("?cross-company=true");
            hasQuery = true;
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            url.Append(hasQuery ? "&" : "?");
            url.Append(_criteria);
        }

        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url));

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);

        var request = await CreateHttpRequestMessageAsync(HttpMethod.Post, url.ToString());
        request.Content = content;

        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("POST failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogInformation("Record created successfully.");
        return result;
    }

    /// <summary>
    /// Create a new record and parse response to typed object
    /// </summary>
    public async Task<T1> AddAsync<T1>(object obj)
    {
        var url = new StringBuilder(_entity);

        var hasQuery = false;
        if (_crossCompany)
        {
            url.Append("?cross-company=true");
            hasQuery = true;
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            url.Append(hasQuery ? "&" : "?");
            url.Append(_criteria);
        }

        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url));

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);

        var request = await CreateHttpRequestMessageAsync(HttpMethod.Post, url.ToString());
        request.Content = content;

        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("POST failed: {StatusCode} - {Result}", response.StatusCode, result);
            if (typeof(T1) == typeof(string))
                return (T1)(object)result;
            return default!;
        }

        _logger.LogInformation("Record created successfully.");

        try
        {
            var deserialized = JsonSerializer.Deserialize<T1>(result);
            if (deserialized is null)
            {
                if (typeof(T1) == typeof(string))
                    return (T1)(object)result;
                return default!;
            }

            return deserialized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deserialization failed");
            if (typeof(T1) == typeof(string))
                return (T1)(object)result;
            return default!;
        }
    }

    /// <summary>
    /// Update record using identities added via AddIdentity (PATCH)
    /// </summary>
    public async Task<string> UpdateAsync(T obj)
    {
        if (!_identities.Any())
            throw new InvalidOperationException(
                "No identity defined for PATCH operation. Please call AddIdentity() or use UpdateAsync(keys, obj).");

        var identityClause = string.Join(",", _identities.Select(kvp =>
            kvp.Value is string str ? $"{kvp.Key}='{str.Replace("'", "''")}'"
            : kvp.Value is DateTime dt ? $"{kvp.Key}={dt:s}Z"
            : $"{kvp.Key}={kvp.Value}"));

        var url = new StringBuilder($"{_entity}({identityClause})");
        var hasQuery = false;
        if (_crossCompany)
        {
            url.Append("?cross-company=true");
            hasQuery = true;
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            url.Append(hasQuery ? "&" : "?");
            url.Append(_criteria);
        }

        _logger.LogInformation("D365 PATCH: {Url}", GetFullUrl(url));

        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
        var request = await CreateHttpRequestMessageAsync(HttpMethod.Patch, url.ToString());
        request.Content = content;

        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PATCH failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogInformation("Record updated successfully.");
        return result;
    }

    /// <summary>
    /// Update record with anonymous key object (PATCH)
    /// </summary>
    public async Task<string> UpdateAsync(object keys, T obj)
    {
        if (keys is null)
            throw new ArgumentNullException(nameof(keys));

        _identities.Clear();
        var keyProps = keys.GetType().GetProperties();

        foreach (var prop in keyProps)
        {
            var val = prop.GetValue(keys);
            if (val is not null)
                _identities[prop.Name] = val;
        }

        return await UpdateAsync(obj);
    }

    /// <summary>
    /// Update record with anonymous object body (partial update via PATCH)
    /// </summary>
    public async Task<string> UpdateAsync(object partialObject)
    {
        if (!_identities.Any())
            throw new InvalidOperationException(
                "No identity defined for PATCH operation. Please call AddIdentity() or use UpdateAsync(keys, obj).");

        var identityClause = string.Join(",", _identities.Select(kvp =>
            kvp.Value is string ? $"{kvp.Key}='{kvp.Value}'" : $"{kvp.Key}={kvp.Value}"));

        var url = new StringBuilder($"{_entity}({identityClause})");
        var hasQuery = false;
        if (_crossCompany)
        {
            url.Append("?cross-company=true");
            hasQuery = true;
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            url.Append(hasQuery ? "&" : "?");
            url.Append(_criteria);
        }

        _logger.LogInformation("D365 PATCH: {Url}", GetFullUrl(url));

        var json = JsonSerializer.Serialize(partialObject, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        });

        var content = new StringContent(json, Encoding.UTF8, MediaTypeNames.Application.Json);
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
        var request = await CreateHttpRequestMessageAsync(HttpMethod.Patch, url.ToString());
        request.Content = content;

        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("PATCH failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogInformation("Record updated successfully.");
        return result;
    }

    /// <summary>
    /// Delete record (DELETE)
    /// </summary>
    public async Task<string> DeleteAsync()
    {
        if (!_identities.Any())
            throw new InvalidOperationException(
                "No identity defined for DELETE operation. Please call AddIdentity().");

        var identityClause = string.Join(",", _identities.Select(kvp =>
            kvp.Value is string str ? $"{kvp.Key}='{str.Replace("'", "''")}'"
            : kvp.Value is DateTime dt ? $"{kvp.Key}={dt:s}Z"
            : $"{kvp.Key}={kvp.Value}"));

        var url = new StringBuilder($"{_entity}({identityClause})");
        var hasQuery = false;
        if (_crossCompany)
        {
            url.Append("?cross-company=true");
            hasQuery = true;
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
        {
            url.Append(hasQuery ? "&" : "?");
            url.Append(_criteria);
        }

        _logger.LogInformation("D365 DELETE: {Url}", GetFullUrl(url));

        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
        var request = await CreateHttpRequestMessageAsync(HttpMethod.Delete, url.ToString());
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DELETE failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogInformation("Record deleted successfully.");
        return result;
    }

    /// <summary>
    /// Get count of matching records
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        // CASE 1: Client-side filtering is active
        if (_clientPredicate != null)
        {
            var count = 0;
            var queryParts = new List<string>();
            if (_crossCompany)
                queryParts.Add("cross-company=true");

            if (!string.IsNullOrWhiteSpace(_criteria))
                queryParts.Add(_criteria);

            var currentUrl = $"{_entity}?{string.Join("&", queryParts)}";

            while (!string.IsNullOrEmpty(currentUrl))
            {
                var (nextUrl, jsonDocument, _) = await GetResponseJsonDocumentAsync(currentUrl, cancellationToken);

                if (jsonDocument is null) break;

                using var doc = jsonDocument;
                if (doc.RootElement.TryGetProperty("value", out JsonElement valueElement) &&
                    valueElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in valueElement.EnumerateArray())
                    {
                        var evaluator = new JsonElementExpressionEvaluator(element);
                        if (evaluator.Evaluate(_clientPredicate))
                        {
                            count++;
                        }
                    }
                }

                currentUrl = nextUrl;
            }

            return count;
        }
        else
        {
            // CASE 2: Server-side filtering only (Fast Path)
            var queryParts = new List<string>();
            if (_crossCompany)
                queryParts.Add("cross-company=true");

            if (!string.IsNullOrWhiteSpace(_criteria))
                queryParts.Add(_criteria);

            // Force Count=true if not present
            if (!queryParts.Any(x => x.Contains("$count=true", StringComparison.OrdinalIgnoreCase)))
                queryParts.Add("$count=true");

            // Force Top=0 to minimize payload
            queryParts.Add("$top=0");

            var baseUrl = $"{_entity}?{string.Join("&", queryParts)}";

            var (_, _, count) = await GetResponseJsonDocumentAsync(baseUrl, cancellationToken);

            return count ?? 0;
        }
    }

    #region Private Helpers

    private async Task<(string NextUrl, JsonDocument? JsonDocumentResult, int? TotalCount)> GetResponseJsonDocumentAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching: {Url}", GetFullUrl(url));

            var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
            var request = await CreateHttpRequestMessageAsync(HttpMethod.Get, url);
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Unauthorized - retrying after token refresh...");
                    return (string.Empty, null, null);
                }

                _logger.LogError("Error fetching data: {StatusCode}", response.StatusCode);
                return (string.Empty, null, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var nextLink = ExtractNextLink(doc.RootElement);
            var countNumber = ExtractCountFromContent(doc.RootElement);

            return (nextLink, doc, countNumber);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error");
            if (ex.InnerException is SocketException socketEx)
            {
                _logger.LogError("Socket error code: {ErrorCode}", socketEx.SocketErrorCode);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error fetching data");
        }

        return (string.Empty, null, null);
    }

    private static string ExtractNextLink(JsonElement root)
    {
        if (root.TryGetProperty("@odata.nextLink", out var nextLink))
            return nextLink.GetString() ?? string.Empty;

        return string.Empty;
    }

    private static int ExtractCountFromContent(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("@odata.count", out var countElement))
                return countElement.GetInt32();
        }
        catch (Exception)
        {
            // Ignore count extraction errors
        }

        return -1;
    }

    private void AppendCriteria(string part)
    {
        if (part.StartsWith("$expand=", StringComparison.OrdinalIgnoreCase))
        {
            var existingIndex = _criteria.IndexOf("$expand=", StringComparison.OrdinalIgnoreCase);
            if (existingIndex >= 0)
            {
                var existingEndIndex = _criteria.IndexOf('&', existingIndex);
                var existingExpand = existingEndIndex >= 0
                    ? _criteria.Substring(existingIndex, existingEndIndex - existingIndex)
                    : _criteria.Substring(existingIndex);

                var newExpandValue = part["$expand=".Length..];
                var existingExpandValue = existingExpand["$expand=".Length..];

                var combinedExpand = $"$expand={existingExpandValue},{newExpandValue}";

                _criteria = existingEndIndex >= 0
                    ? _criteria[..existingIndex] + combinedExpand + _criteria[existingEndIndex..]
                    : _criteria[..existingIndex] + combinedExpand;

                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_criteria))
            _criteria += "&";
        _criteria += part;
    }

    #endregion
}
