using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public sealed class D365TransportException : D365Exception
{
    public D365TransportException(
        string message,
        D365FailureKind failureKind = D365FailureKind.Transport,
        HttpMethod? method = null,
        Uri? requestUri = null,
        string? entityName = null,
        bool isTransient = true,
        D365MutationOutcome mutationOutcome = D365MutationOutcome.NotApplicable,
        Exception? innerException = null)
        : base(
            message,
            ValidateFailureKind(failureKind),
            method: method,
            requestUri: requestUri,
            entityName: entityName,
            isTransient: isTransient,
            mutationOutcome: mutationOutcome,
            innerException: innerException)
    {
    }

    private static D365FailureKind ValidateFailureKind(D365FailureKind failureKind)
    {
        if (failureKind is not (D365FailureKind.Transport or D365FailureKind.Timeout))
            throw new ArgumentOutOfRangeException(nameof(failureKind));

        return failureKind;
    }
}
