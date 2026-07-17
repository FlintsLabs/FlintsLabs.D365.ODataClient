namespace FlintsLabs.D365.ODataClient.Models;

internal sealed record D365AccessToken(
    string Value,
    DateTimeOffset ExpiresOn);
