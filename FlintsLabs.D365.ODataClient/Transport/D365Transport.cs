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
        if (cancellationToken.IsCancellationRequested)
            throw CreateCancellationException(request, false, cancellationToken, null);

        var httpClient = httpClientFactory.CreateClient(options.HttpClientName);
        var requestUri = ResolveRequestUri(httpClient, request.RelativeOrAbsoluteUrl);
        var sendStarted = false;

        try
        {
            var accessToken = await tokenProvider.GetAccessTokenAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.RequestTimeout != Timeout.InfiniteTimeSpan)
            {
                if (options.RequestTimeout <= TimeSpan.Zero)
                    throw new InvalidOperationException("RequestTimeout must be positive or infinite.");

                timeout.CancelAfter(options.RequestTimeout);
            }

            using var message = request.CreateMessage(accessToken);
            sendStarted = true;
            using var response = await httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var headers = CaptureHeaders(response);
            var requestId = ExtractRequestId(headers);
            var mutationOutcome = ClassifyMutationOutcome(request, response.StatusCode);

            TryLog(() => logger.LogInformation(
                "D365 {Method} {Entity} completed with {StatusCode}",
                request.Method,
                request.EntityName,
                response.StatusCode));

            return new D365Response(
                response.StatusCode,
                body,
                headers,
                requestUri,
                requestId,
                mutationOutcome);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw CreateCancellationException(request, sendStarted, cancellationToken, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new D365TransportException(
                $"D365 {request.Method} request timed out after {options.RequestTimeout}.",
                D365FailureKind.Timeout,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new D365TransportException(
                $"D365 {request.Method} request failed before an HTTP response was received.",
                D365FailureKind.Transport,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
        }
        catch (IOException exception)
        {
            throw new D365TransportException(
                $"D365 {request.Method} response could not be read.",
                D365FailureKind.Transport,
                request.Method,
                requestUri,
                request.EntityName,
                mutationOutcome: TransportFailureOutcome(request, sendStarted),
                innerException: exception);
        }
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
        var retryAfter = ReadRetryAfter(response.Headers);
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

    private static TimeSpan? ReadRetryAfter(IReadOnlyDictionary<string, string[]> headers)
    {
        if (!headers.TryGetValue("Retry-After", out var values))
            return null;

        var value = values.FirstOrDefault();
        if (int.TryParse(value, out var seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(value, out var date))
            return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
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
}
