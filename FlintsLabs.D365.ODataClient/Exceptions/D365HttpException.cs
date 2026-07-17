using System.Net;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public class D365HttpException : D365Exception
{
    public D365HttpException(
        string message,
        HttpStatusCode statusCode,
        HttpMethod? method = null,
        Uri? requestUri = null,
        string? entityName = null,
        string? responseBody = null,
        string? d365ErrorCode = null,
        string? d365ErrorMessage = null,
        string? requestId = null,
        bool? isTransient = null,
        D365MutationOutcome mutationOutcome = D365MutationOutcome.NotApplicable,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : this(
            message,
            D365FailureKind.Http,
            statusCode,
            method,
            requestUri,
            entityName,
            responseBody,
            d365ErrorCode,
            d365ErrorMessage,
            requestId,
            isTransient,
            mutationOutcome,
            retryAfter,
            innerException)
    {
    }

    protected D365HttpException(
        string message,
        D365FailureKind failureKind,
        HttpStatusCode statusCode,
        HttpMethod? method,
        Uri? requestUri,
        string? entityName,
        string? responseBody,
        string? d365ErrorCode,
        string? d365ErrorMessage,
        string? requestId,
        bool? isTransient,
        D365MutationOutcome mutationOutcome,
        TimeSpan? retryAfter,
        Exception? innerException)
        : base(
            message,
            failureKind,
            statusCode,
            method,
            requestUri,
            entityName,
            responseBody,
            d365ErrorCode,
            d365ErrorMessage,
            requestId,
            isTransient ?? IsTransientStatus(statusCode),
            mutationOutcome,
            retryAfter,
            innerException)
    {
    }

    public static D365HttpException FromResponse(D365Response response)
    {
        return new D365HttpException(
            $"D365 request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).",
            response.StatusCode,
            requestUri: response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId,
            mutationOutcome: response.MutationOutcome);
    }

    internal static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }
}
