namespace FlintsLabs.D365.ODataClient.Extensions;

public sealed class D365RetryOptions
{
    public int MaxReadRetries { get; set; }
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}
