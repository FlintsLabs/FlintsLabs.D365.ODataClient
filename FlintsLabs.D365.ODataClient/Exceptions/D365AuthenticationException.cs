using System.Net;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public sealed class D365AuthenticationException : D365HttpException
{
    public D365AuthenticationException(
        string message,
        HttpStatusCode statusCode = HttpStatusCode.Unauthorized,
        HttpMethod? method = null,
        Uri? requestUri = null,
        string? entityName = null,
        string? responseBody = null,
        string? d365ErrorCode = null,
        string? d365ErrorMessage = null,
        string? requestId = null,
        D365MutationOutcome mutationOutcome = D365MutationOutcome.NotApplicable,
        Exception? innerException = null)
        : base(
            message,
            D365FailureKind.Authentication,
            statusCode,
            method,
            requestUri,
            entityName,
            responseBody,
            d365ErrorCode,
            d365ErrorMessage,
            requestId,
            false,
            mutationOutcome,
            null,
            innerException)
    {
    }
}
