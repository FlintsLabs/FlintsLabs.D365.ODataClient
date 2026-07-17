using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public sealed class D365OperationCanceledException : OperationCanceledException
{
    public D365OperationCanceledException(
        string message,
        D365MutationOutcome mutationOutcome,
        CancellationToken cancellationToken,
        Exception? innerException = null)
        : base(message, innerException, cancellationToken)
    {
        MutationOutcome = mutationOutcome;
    }

    public D365MutationOutcome MutationOutcome { get; }
}
