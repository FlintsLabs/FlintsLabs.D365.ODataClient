using System.Net;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public sealed class D365ProtocolException : D365Exception
{
    public D365ProtocolException(
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
            D365FailureKind.Protocol,
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

    internal static D365ProtocolException MissingOrInvalidCount(D365Response response)
    {
        return new D365ProtocolException(
            "The D365 response is missing a valid non-negative 64-bit '@odata.count' value.",
            response.StatusCode,
            HttpMethod.Get,
            response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId,
            mutationOutcome: D365MutationOutcome.NotApplicable);
    }

    internal static D365ProtocolException EmptyTypedMutationBody(D365Response response)
    {
        return new D365ProtocolException(
            "The successful D365 mutation response did not contain a body required for typed deserialization.",
            response.StatusCode,
            HttpMethod.Post,
            response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId,
            mutationOutcome: response.MutationOutcome);
    }

    internal static D365ProtocolException EmptyTypedMutationValue(D365Response response)
    {
        return new D365ProtocolException(
            "The successful D365 mutation response deserialized to null.",
            response.StatusCode,
            HttpMethod.Post,
            response.RequestUri,
            responseBody: response.RawBody,
            requestId: response.RequestId,
            mutationOutcome: response.MutationOutcome);
    }
}
