namespace FlintsLabs.D365.ODataClient.Models;

public sealed record D365AccessToken(
    string Value,
    DateTimeOffset ExpiresOn);
