namespace FlintsLabs.D365.ODataClient.OData;

internal sealed record ODataCollectionPage<T>(
    IReadOnlyList<T> Records,
    string? NextLink,
    long? Count);
