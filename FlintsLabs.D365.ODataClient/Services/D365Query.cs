using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Reflection;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Expressions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.OData;
using FlintsLabs.D365.ODataClient.Transport;
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
    private readonly ID365Transport _transport;
    private readonly D365ClientOptions _options;
    private readonly string _entity;
    private string _criteria = string.Empty;
    private bool _crossCompany;
    private readonly Dictionary<string, string> _headerExtension = new();

    // Identity (used for PATCH / DELETE)
    private readonly Dictionary<string, object?> _identities = new();

    // Cached JsonSerializerOptions for serialization (thread-safe, immutable)
    private static readonly JsonSerializerOptions DefaultSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // Client-side filtering & paging
    private Expression<Func<T, bool>>? _clientPredicate;
    private int? _takeCount;
    private Expression<Func<T, bool>>? _wherePredicate;

    private sealed record KeyProperty(PropertyInfo Property, string ODataName);
    private static readonly KeyProperty[] KeyProperties = ResolveKeyProperties();

    public D365Query(
        IHttpClientFactory factory,
        ILogger logger,
        ID365AccessTokenProvider tokenProvider,
        string entity,
        D365ClientOptions options)
        : this(
            factory,
            logger,
            tokenProvider,
            entity,
            options,
            new D365Transport(factory, logger, tokenProvider, options))
    {
    }

    internal D365Query(
        IHttpClientFactory factory,
        ILogger logger,
        ID365AccessTokenProvider tokenProvider,
        string entity,
        D365ClientOptions options,
        ID365Transport transport)
    {
        _httpClientFactory = factory;
        _logger = logger;
        _tokenProvider = tokenProvider;
        _entity = entity;
        _options = options;
        _transport = transport;
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
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));

        // Add extension headers
        foreach (var header in _headerExtension)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        _logger.LogInformation("Sending D365 Request: {Method} {Url}", method, url);
        foreach (var header in request.Headers)
        {
            _logger.LogDebug("Header: {Key}={Value}", header.Key, MaskHeaderValue(header.Key, header.Value));
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

    private static string MaskHeaderValue(string key, IEnumerable<string> values)
    {
        if (string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            return "Bearer ***";
        }

        return string.Join(",", values);
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
        var visitor = new D365ExpressionVisitor(_options.BooleanFormatting);
        var filter = visitor.Translate(predicate.Body);
        AppendCriteria($"$filter={filter}");
        _wherePredicate = predicate;
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
    /// Order by property using LINQ expression (ascending)
    /// </summary>
    /// <typeparam name="TKey">Property type</typeparam>
    /// <param name="keySelector">Property selector expression</param>
    public D365Query<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var propertyName = D365ExpressionHelper.GetPropertyName(keySelector);
        AppendOrderBy(propertyName, "asc");
        return this;
    }

    /// <summary>
    /// Order by property using LINQ expression (descending)
    /// </summary>
    /// <typeparam name="TKey">Property type</typeparam>
    /// <param name="keySelector">Property selector expression</param>
    public D365Query<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var propertyName = D365ExpressionHelper.GetPropertyName(keySelector);
        AppendOrderBy(propertyName, "desc");
        return this;
    }

    /// <summary>
    /// Then order by property using LINQ expression (ascending) for secondary sorting
    /// </summary>
    /// <typeparam name="TKey">Property type</typeparam>
    /// <param name="keySelector">Property selector expression</param>
    public D365Query<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var propertyName = D365ExpressionHelper.GetPropertyName(keySelector);
        AppendThenBy(propertyName, "asc");
        return this;
    }

    /// <summary>
    /// Then order by property using LINQ expression (descending) for secondary sorting
    /// </summary>
    /// <typeparam name="TKey">Property type</typeparam>
    /// <param name="keySelector">Property selector expression</param>
    public D365Query<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var propertyName = D365ExpressionHelper.GetPropertyName(keySelector);
        AppendThenBy(propertyName, "desc");
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
    /// Expand navigation property
    /// </summary>
    public D365Query<T> Expand(string navigationName)
    {
        AppendCriteria($"$expand={navigationName}");
        return this;
    }

    /// <summary>
    /// Expand navigation property using LINQ expression
    /// </summary>
    public D365Query<T> Expand(Expression<Func<T, object>> navigation)
    {
        var navName = D365ExpressionHelper.GetPropertyName(navigation);
        return Expand(navName);
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
        var baseUrl = BuildReadUrl(includeServerTop: true);
        _logger.LogInformation("D365 GET: {Url}", GetFullUrl(baseUrl));

        var records = new List<T>();
        string? currentUrl = baseUrl;
        var nextLinkValidator = new ODataNextLinkValidator(_options.GetBaseUrl());
        var visitedPages = new HashSet<string>(StringComparer.Ordinal);
        var pageCount = 0;

        try
        {
            while (!string.IsNullOrEmpty(currentUrl))
            {
                if (_takeCount.HasValue && records.Count >= _takeCount.Value)
                    break;
                EnsureCanFetchPage(pageCount);

                var pageUri = nextLinkValidator.Resolve(currentUrl);
                if (!visitedPages.Add(pageUri.AbsoluteUri))
                    throw new D365ProtocolException("A loop was detected in D365 pagination links.");

                var page = await GetPageAsync(pageUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
                pageCount++;
                foreach (var item in page.Records)
                {
                    records.Add(item);
                    if (_takeCount.HasValue && records.Count >= _takeCount.Value)
                        break;
                }

                _logger.LogDebug("Fetched chunk (total collected {Count})", records.Count);
                currentUrl = page.NextLink;
            }
        }
        catch (D365Exception exception)
        {
            exception.PartialRecordCount = records.Count;
            throw;
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
        var json = JsonSerializer.Serialize(obj, DefaultSerializerOptions);

        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url));
        _logger.LogDebug("Request Body: {Body}", json);

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

        _logger.LogDebug("Record created successfully.");
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

        var json = JsonSerializer.Serialize(obj, DefaultSerializerOptions);

        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url.ToString()));
        _logger.LogDebug("Request Body: {Body}", json);

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

        _logger.LogDebug("Record created successfully.");
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

        var json = JsonSerializer.Serialize(obj, DefaultSerializerOptions);

        _logger.LogInformation("D365 POST: {Url}", GetFullUrl(url.ToString()));
        _logger.LogDebug("Request Body: {Body}", json);

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

        _logger.LogDebug("Record created successfully.");

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
        EnsureIdentitiesForWrite();

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

        var json = JsonSerializer.Serialize(obj, DefaultSerializerOptions);

        _logger.LogInformation("D365 PATCH: {Url}", GetFullUrl(url.ToString()));
        _logger.LogDebug("Request Body: {Body}", json);

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

        _logger.LogDebug("Record updated successfully.");
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
        EnsureIdentitiesForWrite();

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

        var json = JsonSerializer.Serialize(partialObject, DefaultSerializerOptions);

        _logger.LogInformation("D365 PATCH: {Url}", GetFullUrl(url.ToString()));
        _logger.LogDebug("Request Body: {Body}", json);

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

        _logger.LogDebug("Record updated successfully.");
        return result;
    }

    /// <summary>
    /// Delete record (DELETE)
    /// </summary>
    public async Task<string> DeleteAsync()
    {
        EnsureIdentitiesForWrite();

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

        _logger.LogInformation("D365 DELETE: {Url}", GetFullUrl(url.ToString()));

        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName);
        var request = await CreateHttpRequestMessageAsync(HttpMethod.Delete, url.ToString());
        var response = await httpClient.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DELETE failed: {StatusCode} - {Result}", response.StatusCode, result);
            return result;
        }

        _logger.LogDebug("Record deleted successfully.");
        return result;
    }

    /// <summary>
    /// Get count of matching records
    /// </summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return checked((int)await LongCountAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Get a 64-bit count of matching records.
    /// </summary>
    public async Task<long> LongCountAsync(CancellationToken cancellationToken = default)
    {
        if (_clientPredicate != null)
        {
            long count = 0;
            var queryParts = new List<string>();
            if (_crossCompany)
                queryParts.Add("cross-company=true");

            if (!string.IsNullOrWhiteSpace(_criteria))
                queryParts.Add(_criteria);

            string? currentUrl = BuildUrl(queryParts);
            var nextLinkValidator = new ODataNextLinkValidator(_options.GetBaseUrl());
            var visitedPages = new HashSet<string>(StringComparer.Ordinal);
            var pageCount = 0;

            try
            {
                while (!string.IsNullOrEmpty(currentUrl))
                {
                    EnsureCanFetchPage(pageCount);
                    var pageUri = nextLinkValidator.Resolve(currentUrl);
                    if (!visitedPages.Add(pageUri.AbsoluteUri))
                        throw new D365ProtocolException("A loop was detected in D365 pagination links.");

                    var page = await GetPageAsync(pageUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
                    pageCount++;
                    count = checked(count + page.Records.Count);
                    currentUrl = page.NextLink;
                }
            }
            catch (D365Exception exception)
            {
                exception.PartialRecordCount = count;
                throw;
            }

            return count;
        }
        else
        {
            var queryParts = new List<string>();
            if (_crossCompany)
                queryParts.Add("cross-company=true");

            if (!string.IsNullOrWhiteSpace(_criteria))
                queryParts.Add(_criteria);

            if (!queryParts.Any(x => x.Contains("$count=true", StringComparison.OrdinalIgnoreCase)))
                queryParts.Add("$count=true");

            queryParts.Add("$top=0");

            var page = await GetPageAsync(
                    BuildUrl(queryParts),
                    cancellationToken,
                    requireCount: true)
                .ConfigureAwait(false);
            return page.Count!.Value;
        }
    }

    #region Private Helpers

    private async Task<ODataCollectionPage<T>> GetPageAsync(
        string url,
        CancellationToken cancellationToken = default,
        bool requireCount = false)
    {
        var request = new D365Request(
            HttpMethod.Get,
            url,
            null,
            _entity,
            new Dictionary<string, string>(_headerExtension, StringComparer.OrdinalIgnoreCase));
        var response = await _transport
            .SendEnsuredAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return DeserializePage(response, requireCount);
    }

    private ODataCollectionPage<T> DeserializePage(
        D365Response response,
        bool requireCount)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.RawBody);
        }
        catch (JsonException exception)
        {
            throw CreateSerializationException(
                "The D365 response body is not valid JSON.",
                response,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw CreateProtocolException("The D365 response must be a JSON object.", response);
            if (root.TryGetProperty("error", out _))
                throw CreateProtocolException("A successful D365 response contained an error envelope.", response);
            if (!root.TryGetProperty("value", out var valueElement))
                throw CreateProtocolException("The D365 collection response is missing the 'value' property.", response);
            if (valueElement.ValueKind != JsonValueKind.Array)
                throw CreateProtocolException("The D365 collection response 'value' property must be an array.", response);

            var records = new List<T>();
            foreach (var element in valueElement.EnumerateArray())
            {
                if (_clientPredicate is not null)
                {
                    var evaluator = new JsonElementExpressionEvaluator(element);
                    if (!evaluator.Evaluate(_clientPredicate))
                        continue;
                }

                try
                {
                    var item = element.Deserialize<T>();
                    if (item is null)
                    {
                        throw CreateSerializationException(
                            "A D365 collection record deserialized to null.",
                            response);
                    }

                    records.Add(item);
                }
                catch (D365SerializationException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    throw CreateSerializationException(
                        $"A D365 collection record could not be deserialized as {typeof(T).Name}.",
                        response,
                        exception);
                }
            }

            var nextLink = ReadNextLink(root, response);
            var count = ReadCount(root, response, requireCount);
            return new ODataCollectionPage<T>(records, nextLink, count);
        }
    }

    private static string? ReadNextLink(JsonElement root, D365Response response)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var nextLink))
            return null;
        if (nextLink.ValueKind == JsonValueKind.Null)
            return null;
        if (nextLink.ValueKind != JsonValueKind.String)
            throw CreateProtocolException("The '@odata.nextLink' property must be a string.", response);

        var value = nextLink.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static long? ReadCount(
        JsonElement root,
        D365Response response,
        bool required)
    {
        if (!root.TryGetProperty("@odata.count", out var count))
        {
            if (required)
                throw D365ProtocolException.MissingOrInvalidCount(response);
            return null;
        }

        if (count.ValueKind == JsonValueKind.Number
            && count.TryGetInt64(out var number)
            && number >= 0)
        {
            return number;
        }

        throw D365ProtocolException.MissingOrInvalidCount(response);
    }

    private static D365SerializationException CreateSerializationException(
        string message,
        D365Response response,
        Exception? innerException = null)
    {
        return new D365SerializationException(
            message,
            response.StatusCode,
            HttpMethod.Get,
            response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId,
            innerException: innerException);
    }

    private static D365ProtocolException CreateProtocolException(
        string message,
        D365Response response)
    {
        return new D365ProtocolException(
            message,
            response.StatusCode,
            HttpMethod.Get,
            response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId);
    }

    private string BuildReadUrl(bool includeServerTop)
    {
        var queryParts = new List<string>();
        if (_crossCompany)
            queryParts.Add("cross-company=true");
        if (!string.IsNullOrWhiteSpace(_criteria))
            queryParts.Add(_criteria);
        if (includeServerTop && _clientPredicate is null && _takeCount.HasValue)
            queryParts.Add($"$top={_takeCount}");

        return BuildUrl(queryParts);
    }

    private string BuildUrl(IReadOnlyCollection<string> queryParts)
    {
        return queryParts.Count == 0
            ? _entity
            : $"{_entity}?{string.Join("&", queryParts)}";
    }

    private void EnsureCanFetchPage(int fetchedPageCount)
    {
        if (_options.MaxPages <= 0)
            throw new D365ProtocolException("D365 MaxPages must be greater than zero.");
        if (fetchedPageCount >= _options.MaxPages)
        {
            throw new D365ProtocolException(
                $"D365 pagination exceeded the configured MaxPages limit of {_options.MaxPages}.");
        }
    }

    private void AppendOrderBy(string property, string direction)
    {
        var orderByClause = $"$orderby={property} {direction}";
        
        // Check if $orderby already exists
        var existingIndex = _criteria.IndexOf("$orderby=", StringComparison.OrdinalIgnoreCase);
        if (existingIndex >= 0)
        {
            // Replace existing $orderby with new one (OrderBy resets the sort)
            var existingEndIndex = _criteria.IndexOf('&', existingIndex);
            _criteria = existingEndIndex >= 0
                ? _criteria[..existingIndex] + orderByClause + _criteria[existingEndIndex..]
                : _criteria[..existingIndex] + orderByClause;
        }
        else
        {
            AppendCriteria(orderByClause);
        }
    }

    private void AppendThenBy(string property, string direction)
    {
        // Check if $orderby already exists
        var existingIndex = _criteria.IndexOf("$orderby=", StringComparison.OrdinalIgnoreCase);
        if (existingIndex >= 0)
        {
            // Find end of existing $orderby
            var existingEndIndex = _criteria.IndexOf('&', existingIndex);
            var insertPosition = existingEndIndex >= 0 ? existingEndIndex : _criteria.Length;
            
            // Append to existing $orderby with comma separator
            var appendPart = $",{property} {direction}";
            _criteria = _criteria.Insert(insertPosition, appendPart);
        }
        else
        {
            // No existing $orderby, treat as primary OrderBy
            AppendOrderBy(property, direction);
        }
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

    private void EnsureIdentitiesForWrite()
    {
        if (_identities.Any())
            return;

        if (TryPopulateIdentitiesFromWhere())
        {
            // Clear filter criteria used only for key lookup to avoid invalid PATCH/DELETE query strings.
            _criteria = string.Empty;
            return;
        }

        throw new InvalidOperationException(
            "No identity defined for write operation. Please call AddIdentity(), use UpdateAsync(keys, obj), " +
            "or add [OdataKey] attribute and specify key equality in Where(...).");
    }

    private bool TryPopulateIdentitiesFromWhere()
    {
        if (_wherePredicate == null)
            return false;

        if (KeyProperties.Length == 0)
            throw new InvalidOperationException(
                $"No [OdataKey] attribute found on {typeof(T).Name}. " +
                "Add [OdataKey] to the key property, or use AddIdentity() explicitly.");

        var keyValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var nonKeyFound = false;

        if (!TryExtractKeyValues(_wherePredicate.Body, keyValues, ref nonKeyFound))
            throw new InvalidOperationException(
                "Where(...) for UpdateAsync/DeleteAsync must use equality on key properties only.");

        if (nonKeyFound)
            throw new InvalidOperationException(
                "Where(...) contains non-key filters. Use AddIdentity(...) or limit Where(...) to key equality only.");

        var missingKeys = KeyProperties
            .Select(k => k.ODataName)
            .Where(k => !keyValues.ContainsKey(k))
            .ToList();

        if (missingKeys.Count > 0)
            throw new InvalidOperationException(
                $"Missing key(s) in Where(...) for write operation: {string.Join(", ", missingKeys)}");

        foreach (var kvp in keyValues)
        {
            _identities[kvp.Key] = kvp.Value;
        }

        return true;
    }

    private static bool TryExtractKeyValues(Expression expr, Dictionary<string, object?> keyValues, ref bool nonKeyFound)
    {
        expr = StripConvert(expr);

        if (expr is BinaryExpression binary)
        {
            if (binary.NodeType == ExpressionType.AndAlso)
            {
                var leftOk = TryExtractKeyValues(binary.Left, keyValues, ref nonKeyFound);
                var rightOk = TryExtractKeyValues(binary.Right, keyValues, ref nonKeyFound);
                return leftOk && rightOk;
            }

            if (binary.NodeType == ExpressionType.Equal)
            {
                return TryExtractKeyEquality(binary.Left, binary.Right, keyValues, ref nonKeyFound);
            }
        }

        return false;
    }

    private static bool TryExtractKeyEquality(
        Expression left,
        Expression right,
        Dictionary<string, object?> keyValues,
        ref bool nonKeyFound)
    {
        left = StripConvert(left);
        right = StripConvert(right);

        if (TryGetKeyProperty(left, out var keyProp))
        {
            var value = EvaluateExpression(right);
            keyValues[keyProp.ODataName] = ConvertToPropertyType(value, keyProp.Property);
            return true;
        }

        if (TryGetKeyProperty(right, out keyProp))
        {
            var value = EvaluateExpression(left);
            keyValues[keyProp.ODataName] = ConvertToPropertyType(value, keyProp.Property);
            return true;
        }

        if (IsParameterMember(left) || IsParameterMember(right))
        {
            nonKeyFound = true;
        }

        return false;
    }

    private static bool TryGetKeyProperty(Expression expr, out KeyProperty keyProperty)
    {
        keyProperty = null!;
        if (!IsParameterMember(expr, out var memberInfo))
            return false;

        if (memberInfo is PropertyInfo prop)
        {
            var isKey = prop.GetCustomAttribute<OdataKeyAttribute>() != null;
            if (!isKey)
                return false;

            var odataName = GetJsonName(prop);
            keyProperty = new KeyProperty(prop, odataName);
            return true;
        }

        return false;
    }

    private static bool IsParameterMember(Expression expr)
    {
        return IsParameterMember(expr, out _);
    }

    private static bool IsParameterMember(Expression expr, out MemberInfo? member)
    {
        member = null;
        if (expr is MemberExpression memberExpr && memberExpr.Expression is ParameterExpression)
        {
            member = memberExpr.Member;
            return true;
        }
        return false;
    }

    private static Expression StripConvert(Expression expr)
    {
        while (expr is UnaryExpression u &&
               (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked))
        {
            expr = u.Operand;
        }
        return expr;
    }

    private static object? EvaluateExpression(Expression expr)
    {
        expr = StripConvert(expr);

        if (expr is ConstantExpression c)
            return c.Value;

        if (expr is MemberExpression m)
            return GetValue(m);

        return Expression.Lambda(expr).Compile().DynamicInvoke();
    }

    private static object? GetValue(MemberExpression member)
    {
        if (member.Member is FieldInfo field)
        {
            var target = member.Expression switch
            {
                MemberExpression inner => GetValue(inner),
                ConstantExpression constExpr => constExpr.Value,
                _ => null
            };

            return field.IsStatic ? field.GetValue(null) : field.GetValue(target);
        }

        if (member.Member is PropertyInfo prop)
        {
            var target = member.Expression switch
            {
                MemberExpression inner => GetValue(inner),
                ConstantExpression constExpr => constExpr.Value,
                _ => null
            };

            if (prop.GetMethod?.IsStatic == true)
                return prop.GetValue(null);

            if (target == null && prop.DeclaringType?.IsValueType == true)
            {
                var lambda = Expression.Lambda(member);
                return lambda.Compile().DynamicInvoke();
            }

            return prop.GetValue(target);
        }

        return null;
    }

    private static object? ConvertToPropertyType(object? value, PropertyInfo property)
    {
        if (value == null)
            return null;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        if (targetType == typeof(Guid))
        {
            if (value is Guid g) return g;
            if (value is string s) return Guid.Parse(s);
        }

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value.ToString() ?? string.Empty, true);

        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static string GetJsonName(MemberInfo member)
    {
        var attr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? member.Name;
    }

    private static KeyProperty[] ResolveKeyProperties()
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        return props
            .Where(p => p.GetCustomAttribute<OdataKeyAttribute>() != null)
            .Select(p => new KeyProperty(p, GetJsonName(p)))
            .ToArray();
    }

    #endregion
}
