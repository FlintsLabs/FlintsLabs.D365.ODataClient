using System.Globalization;
using System.Net;
using FlintsLabs.D365.ODataClient.Extensions;

namespace FlintsLabs.D365.ODataClient.Transport;

internal static class D365RetryPolicy
{
    public static bool CanRetryRead(
        D365Request request,
        int completedRetries,
        D365RetryOptions options)
    {
        return (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            && completedRetries < options.MaxReadRetries;
    }

    public static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    public static TimeSpan CalculateDelay(
        IReadOnlyDictionary<string, string[]> headers,
        int retryNumber,
        D365RetryOptions options,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryNumber, 1);
        options.Validate();

        if (TryReadRetryAfter(headers, now, out var retryAfter))
            return Min(retryAfter, options.MaxDelay);

        var multiplier = Math.Pow(2, Math.Min(retryNumber - 1, 30));
        var delayMilliseconds = Math.Min(
            options.BaseDelay.TotalMilliseconds * multiplier,
            options.MaxDelay.TotalMilliseconds);
        if (options.UseJitter)
        {
            delayMilliseconds *= 0.5 + Random.Shared.NextDouble();
            delayMilliseconds = Math.Min(delayMilliseconds, options.MaxDelay.TotalMilliseconds);
        }

        return TimeSpan.FromMilliseconds(delayMilliseconds);
    }

    public static TimeSpan? ReadRetryAfter(
        IReadOnlyDictionary<string, string[]> headers,
        DateTimeOffset now)
    {
        return TryReadRetryAfter(headers, now, out var retryAfter)
            ? retryAfter
            : null;
    }

    private static bool TryReadRetryAfter(
        IReadOnlyDictionary<string, string[]> headers,
        DateTimeOffset now,
        out TimeSpan retryAfter)
    {
        retryAfter = default;
        if (!headers.TryGetValue("Retry-After", out var values))
            return false;

        var value = values.FirstOrDefault();
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0)
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date))
        {
            retryAfter = date > now ? date - now : TimeSpan.Zero;
            return true;
        }

        return false;
    }

    private static TimeSpan Min(TimeSpan value, TimeSpan maximum)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        return value <= maximum ? value : maximum;
    }
}
