using System.Net;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public sealed class D365SerializationException : D365Exception
{
    public D365SerializationException(
        string message,
        HttpStatusCode? statusCode = null,
        HttpMethod? method = null,
        Uri? requestUri = null,
        string? entityName = null,
        string? responseBody = null,
        string? requestId = null,
        D365MutationOutcome mutationOutcome = D365MutationOutcome.NotApplicable,
        Exception? innerException = null)
        : base(
            message,
            D365FailureKind.Serialization,
            statusCode,
            method,
            requestUri,
            entityName,
            responseBody,
            requestId: requestId,
            mutationOutcome: mutationOutcome,
            innerException: innerException)
    {
    }
}
