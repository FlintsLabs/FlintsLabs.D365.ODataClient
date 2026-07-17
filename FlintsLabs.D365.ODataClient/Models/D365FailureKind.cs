namespace FlintsLabs.D365.ODataClient.Models;

public enum D365FailureKind
{
    Http,
    Authentication,
    Transport,
    Timeout,
    Serialization,
    Protocol
}
