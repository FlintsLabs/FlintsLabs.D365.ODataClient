using System.Net;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Exceptions;

public abstract class D365Exception : Exception
{
    protected D365Exception(
        string message,
        D365FailureKind failureKind,
        HttpStatusCode? statusCode = null,
        HttpMethod? method = null,
        Uri? requestUri = null,
        string? entityName = null,
        string? responseBody = null,
        string? d365ErrorCode = null,
        string? d365ErrorMessage = null,
        string? requestId = null,
        bool isTransient = false,
        D365MutationOutcome mutationOutcome = D365MutationOutcome.NotApplicable,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        StatusCode = statusCode;
        Method = method;
        RequestUri = requestUri;
        EntityName = entityName;
        ResponseBody = responseBody;
        D365ErrorCode = d365ErrorCode;
        D365ErrorMessage = d365ErrorMessage;
        RequestId = requestId;
        IsTransient = isTransient;
        MutationOutcome = mutationOutcome;
        RetryAfter = retryAfter;
    }

    public D365FailureKind FailureKind { get; }
    public HttpStatusCode? StatusCode { get; }
    public HttpMethod? Method { get; }
    public Uri? RequestUri { get; }
    public string? EntityName { get; }
    public string? ResponseBody { get; }
    public string? D365ErrorCode { get; }
    public string? D365ErrorMessage { get; }
    public string? RequestId { get; }
    public bool IsTransient { get; }
    public D365MutationOutcome MutationOutcome { get; }
    public TimeSpan? RetryAfter { get; }
    public long PartialRecordCount { get; internal set; }
}
