using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;

namespace FlintsLabs.D365.ODataClient.Models;

public sealed record D365Response(
    HttpStatusCode StatusCode,
    string RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    Uri RequestUri,
    string? RequestId,
    D365MutationOutcome MutationOutcome)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    public void EnsureSuccessStatusCode()
    {
        if (!IsSuccessStatusCode)
            throw D365HttpException.FromResponse(this);
    }
}

public sealed record D365Response<T>(
    HttpStatusCode StatusCode,
    T? Value,
    string RawBody,
    IReadOnlyDictionary<string, string[]> Headers,
    Uri RequestUri,
    string? RequestId,
    D365MutationOutcome MutationOutcome)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;

    public void EnsureSuccessStatusCode()
    {
        if (IsSuccessStatusCode)
            return;

        throw new D365HttpException(
            $"D365 request failed with HTTP {(int)StatusCode} ({StatusCode}).",
            StatusCode,
            requestUri: RequestUri,
            responseBody: RawBody,
            requestId: RequestId,
            mutationOutcome: MutationOutcome);
    }
}
