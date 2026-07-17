using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Services;

internal sealed class D365Client(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    ID365AccessTokenProvider tokenProvider,
    D365ClientOptions options,
    ID365Transport transport) : ID365Client
{
    private static readonly ConcurrentDictionary<Enum, string> EntityNames = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public D365Query<T> Entity<T>(string entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        return new D365Query<T>(
            httpClientFactory,
            logger,
            tokenProvider,
            entity,
            options,
            transport);
    }

    public D365Query<T> Entity<T>(Enum entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Entity<T>(EntityNames.GetOrAdd(entity, ResolveEntityName));
    }

    public Task<D365Response> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeUrl);

        string? jsonPayload = null;
        if (payload is not null)
        {
            try
            {
                jsonPayload = JsonSerializer.Serialize(
                    payload,
                    payload.GetType(),
                    SerializerOptions);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new D365SerializationException(
                    "The raw D365 request payload could not be serialized.",
                    method: method,
                    mutationOutcome: IsMutation(method)
                        ? D365MutationOutcome.NotSent
                        : D365MutationOutcome.NotApplicable,
                    innerException: exception);
            }
        }

        var request = new D365Request(
            method,
            relativeUrl,
            jsonPayload,
            null,
            new Dictionary<string, string>());
        return transport.SendRawAsync(request, cancellationToken);
    }

    private static string ResolveEntityName(Enum entity)
    {
        var field = entity.GetType().GetField(entity.ToString());
        return field?.GetCustomAttribute<DescriptionAttribute>()?.Description
               ?? entity.ToString();
    }

    private static bool IsMutation(HttpMethod method)
    {
        return method == HttpMethod.Post
               || method == HttpMethod.Patch
               || method == HttpMethod.Delete;
    }
}
