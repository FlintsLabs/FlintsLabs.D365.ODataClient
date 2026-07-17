using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;

internal sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<CapturedHttpRequest, CancellationToken, Task<HttpResponseMessage>>> _steps = new();
    private readonly ConcurrentQueue<CapturedHttpRequest> _requests = new();

    public IReadOnlyList<CapturedHttpRequest> Requests => _requests.ToArray();

    public void Enqueue(HttpStatusCode statusCode, string body = "")
    {
        Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
    }

    public void EnqueueException(Exception exception)
    {
        Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    public void Enqueue(Func<CapturedHttpRequest, CancellationToken, Task<HttpResponseMessage>> step)
    {
        _steps.Enqueue(step);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_steps.TryDequeue(out var step))
            throw new InvalidOperationException("No queued HTTP response.");

        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headerPairs = request.Headers;
        if (request.Content is not null)
            headerPairs = headerPairs.Concat(request.Content.Headers);

        var headers = headerPairs
            .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedHttpRequest(request.Method, request.RequestUri, headers, body);
        _requests.Enqueue(captured);

        var response = await step(captured, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }
}
