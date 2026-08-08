using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

/// <summary>
/// Represents a failure while acquiring an access token from Microsoft Entra ID.
/// </summary>
public sealed class D365TokenAcquisitionException : D365Exception
{
    public D365TokenAcquisitionException(
        string message,
        D365AuthType authType,
        bool isTransient = false,
        Exception? innerException = null)
        : base(
            message,
            D365FailureKind.Authentication,
            isTransient: isTransient,
            innerException: innerException)
    {
        AuthType = authType;
    }

    /// <summary>
    /// Authentication method that failed to acquire a token.
    /// </summary>
    public D365AuthType AuthType { get; }
}
