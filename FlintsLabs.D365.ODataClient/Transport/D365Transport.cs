using System.Diagnostics;
using System.Net;
using System.Text;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Logging;

namespace FlintsLabs.D365.ODataClient.Transport;

internal sealed class D365Transport(
    IHttpClientFactory httpClientFactory,
    ILogger logger,
    ID365AccessTokenProvider tokenProvider,
    D365ClientOptions options) : ID365Transport
{
    private static readonly string[] RequestIdHeaders =
    [
        "x-ms-service-request-id",
        "x-ms-correlation-request-id",
        "request-id",
        "REQ_ID",
        "ActivityId"
    ];

    public async Task<D365Response> SendRawAsync(
        D365Request request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (options.Retry is null)
            throw new InvalidOperationException("D365 Retry options are not configured.");
        options.Retry.Validate();

        if (cancellationToken.IsCancellationRequested)
            throw CreateCancellationException(request, false, cancellationToken, null);

        var httpClient = httpClientFactory.CreateClient(options.HttpClientName);
        var requestUri = ResolveRequestUri(httpClient, request.RelativeOrAbsoluteUrl);
        var sendStarted = false;
        var operationTimer = Stopwatch.StartNew();

        try
        {
            var accessToken = await tokenProvider
                .GetAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var completedReadRetries = 0;
            var authenticationRetried = false;
            while (true)
            {
                D365Response response;
                try
                {
                    sendStarted = true;
                    response = await SendAttemptAsync(
                            httpClient,
                            request,
                            accessToken.Value,
                            requestUri,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested
                    && D365RetryPolicy.CanRetryRead(request, completedReadRetries, options.Retry))
                {
                    completedReadRetries++;
                    await DelayForRetryAsync(
                            request,
                            completedReadRetries,
                            EmptyHeaders,
                            "timeout",
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or IOException
                    && D365RetryPolicy.CanRetryRead(request, completedReadRetries, options.Retry))
                {
                    completedReadRetries++;
                    await DelayForRetryAsync(
                            request,
                            completedReadRetries,
                            EmptyHeaders,
                            "transport",
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !authenticationRetried)
                {
                    authenticationRetried = true;
                    accessToken = await tokenProvider
                        .RefreshAccessTokenAsync(accessToken.Value, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }

                if (D365RetryPolicy.IsRetryableStatus(response.StatusCode)
                    && D365RetryPolicy.CanRetryRead(request, completedReadRetries, options.Retry))
                {
                    completedReadRetries++;
                    await DelayForRetryAsync(
                            request,
                            completedReadRetries,
                            response.Headers,
                            $"HTTP {(int)response.StatusCode}",
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                return response;
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            var cancellationException = CreateCancellationException(
                request,
                sendStarted,
                cancellationToken,
                exception);
            TryLogFailure(request, cancellationException, operationTimer.Elapsed);
            throw cancellationException;
        }
        catch (OperationCanceledException exception)
        {
            var transportException = new D365TransportException(
                $"D365 {request.Method} request timed out after {options.RequestTimeout}.",
                D365FailureKind.Timeout,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
            TryLogFailure(request, transportException, operationTimer.Elapsed);
            throw transportException;
        }
        catch (HttpRequestException exception)
        {
            var transportException = new D365TransportException(
                $"D365 {request.Method} request failed before an HTTP response was received.",
                D365FailureKind.Transport,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
            TryLogFailure(request, transportException, operationTimer.Elapsed);
            throw transportException;
        }
        catch (IOException exception)
        {
            var transportException = new D365TransportException(
                $"D365 {request.Method} response could not be read.",
                D365FailureKind.Transport,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
            TryLogFailure(request, transportException, operationTimer.Elapsed);
            throw transportException;
        }
    }

    private static readonly IReadOnlyDictionary<string, string[]> EmptyHeaders =
        new Dictionary<string, string[]>();

    private async Task DelayForRetryAsync(
        D365Request request,
        int retryNumber,
        IReadOnlyDictionary<string, string[]> headers,
        string reason,
        CancellationToken cancellationToken)
    {
        var delay = D365RetryPolicy.CalculateDelay(
            headers,
            retryNumber,
            options.Retry,
            DateTimeOffset.UtcNow);
        TryLog(() => logger.LogInformation(
            "D365 {Method} {Entity} retry {RetryNumber} after {DelayMs} ms due to {Reason}",
            request.Method,
            request.EntityName,
            retryNumber,
            delay.TotalMilliseconds,
            reason));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private async Task<D365Response> SendAttemptAsync(
        HttpClient httpClient,
        D365Request request,
        string accessToken,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        var attemptTimer = Stopwatch.StartNew();
        TryLog(() => logger.LogDebug(
            "D365 {Method} {Entity} route {Route}",
            request.Method,
            request.EntityName,
            D365LogSanitizer.Sanitize(requestUri)));

        using var timeout = CreateTimeoutTokenSource(cancellationToken);
        using var message = request.CreateMessage(accessToken);
        using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token)
            .ConfigureAwait(false);
        var body = await response.Content
            .ReadAsStringAsync(timeout.Token)
            .ConfigureAwait(false);
        var headers = CaptureHeaders(response);
        var requestId = ExtractRequestId(headers);
        var mutationOutcome = ClassifyMutationOutcome(request, response.StatusCode);
        var isTransient = D365HttpException.IsTransientStatus(response.StatusCode);

        TryLog(() => logger.LogInformation(
            "D365 {Method} {Entity} completed with {StatusCode} in {DurationMs} ms; RequestId={RequestId}; Transient={IsTransient}; MutationOutcome={MutationOutcome}",
            request.Method,
            request.EntityName,
            response.StatusCode,
            attemptTimer.Elapsed.TotalMilliseconds,
            requestId,
            isTransient,
            mutationOutcome));

        return new D365Response(
            response.StatusCode,
            body,
            headers,
            requestUri,
            requestId,
            mutationOutcome);
    }

    private CancellationTokenSource CreateTimeoutTokenSource(
        CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.RequestTimeout == Timeout.InfiniteTimeSpan)
            return timeout;
        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            timeout.Dispose();
            throw new InvalidOperationException("RequestTimeout must be positive or infinite.");
        }

        timeout.CancelAfter(options.RequestTimeout);
        return timeout;
    }

    public async Task<D365Response> SendEnsuredAsync(
        D365Request request,
        CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return response;

        throw CreateHttpException(request, response);
    }

    private D365Exception CreateHttpException(D365Request request, D365Response response)
    {
        var error = D365ErrorParser.Parse(response.RawBody);
        var responseBody = TruncateUtf8(response.RawBody, options.MaxErrorBodyBytes);
        var retryAfter = D365RetryPolicy.ReadRetryAfter(response.Headers, DateTimeOffset.UtcNow);
        var message = error.Message is null
            ? $"D365 request failed with HTTP {(int)response.StatusCode} ({response.StatusCode})."
            : $"D365 request failed: {error.Message}";

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new D365AuthenticationException(
                message,
                response.StatusCode,
                request.Method,
                response.RequestUri,
                request.EntityName,
                responseBody,
                error.Code,
                error.Message,
                response.RequestId,
                response.MutationOutcome);
        }

        return new D365HttpException(
            message,
            response.StatusCode,
            request.Method,
            response.RequestUri,
            request.EntityName,
            responseBody,
            error.Code,
            error.Message,
            response.RequestId,
            retryAfter: retryAfter,
            mutationOutcome: response.MutationOutcome);
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            headers[header.Key] = header.Value.ToArray();
        return headers;
    }

    private static string? ExtractRequestId(IReadOnlyDictionary<string, string[]> headers)
    {
        foreach (var headerName in RequestIdHeaders)
        {
            if (headers.TryGetValue(headerName, out var values))
                return values.FirstOrDefault();
        }

        return null;
    }

    private static D365MutationOutcome ClassifyMutationOutcome(
        D365Request request,
        HttpStatusCode statusCode)
    {
        if (!request.IsMutation)
            return D365MutationOutcome.NotApplicable;
        if ((int)statusCode is >= 200 and <= 299)
            return D365MutationOutcome.SucceededOrAccepted;
        if (statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500)
            return D365MutationOutcome.Unknown;
        return D365MutationOutcome.Rejected;
    }

    private static D365MutationOutcome TransportFailureOutcome(D365Request request, bool sendStarted)
    {
        if (!request.IsMutation)
            return D365MutationOutcome.NotApplicable;
        return sendStarted ? D365MutationOutcome.Unknown : D365MutationOutcome.NotSent;
    }

    private static D365OperationCanceledException CreateCancellationException(
        D365Request request,
        bool sendStarted,
        CancellationToken cancellationToken,
        Exception? innerException)
    {
        return new D365OperationCanceledException(
            $"D365 {request.Method} request was canceled by the caller.",
            TransportFailureOutcome(request, sendStarted),
            cancellationToken,
            innerException);
    }

    private static Uri ResolveRequestUri(HttpClient httpClient, string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absolute))
            return absolute;
        if (httpClient.BaseAddress is null)
            throw new InvalidOperationException("D365 HttpClient BaseAddress is not configured.");
        return new Uri(httpClient.BaseAddress, relativeOrAbsoluteUrl);
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (maxBytes <= 0 || string.IsNullOrEmpty(value))
            return string.Empty;
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        var chars = 0;
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maxBytes)
                break;
            bytes += runeBytes;
            chars += rune.Utf16SequenceLength;
        }

        return value[..chars];
    }

    private static void TryLog(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // Logging must never replace request results or exceptions.
        }
    }

    private void TryLogFailure(
        D365Request request,
        D365TransportException exception,
        TimeSpan duration)
    {
        TryLog(() => logger.LogWarning(
            "D365 {Method} {Entity} failed after {DurationMs} ms; FailureKind={FailureKind}; Transient={IsTransient}; MutationOutcome={MutationOutcome}",
            request.Method,
            request.EntityName,
            duration.TotalMilliseconds,
            exception.FailureKind,
            exception.IsTransient,
            exception.MutationOutcome));
    }

    private void TryLogFailure(
        D365Request request,
        D365OperationCanceledException exception,
        TimeSpan duration)
    {
        TryLog(() => logger.LogWarning(
            "D365 {Method} {Entity} canceled after {DurationMs} ms; MutationOutcome={MutationOutcome}",
            request.Method,
            request.EntityName,
            duration.TotalMilliseconds,
            exception.MutationOutcome));
    }
}
