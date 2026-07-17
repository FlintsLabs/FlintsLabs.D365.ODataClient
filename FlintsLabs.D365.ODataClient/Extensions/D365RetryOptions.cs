namespace FlintsLabs.D365.ODataClient.Extensions;

public sealed class D365RetryOptions
{
    public int MaxReadRetries { get; set; }
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;

    internal void Validate()
    {
        if (MaxReadRetries < 0)
            throw new ArgumentOutOfRangeException(
                nameof(MaxReadRetries),
                "MaxReadRetries cannot be negative.");
        if (BaseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(BaseDelay),
                "BaseDelay must be greater than zero.");
        if (MaxDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(MaxDelay),
                "MaxDelay must be greater than zero.");
        if (BaseDelay > MaxDelay)
            throw new ArgumentOutOfRangeException(
                nameof(BaseDelay),
                "BaseDelay cannot be greater than MaxDelay.");
    }
}
