using System.Net;
using FlintsLabs.D365.ODataClient.Exceptions;
using FlintsLabs.D365.ODataClient.Models;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.Transport;

public class PublicContractTests
{
    private static readonly Uri RequestUri = new("https://example.test/data/Entities");

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public void Response_ComputesSuccessFromStatusCode(HttpStatusCode statusCode, bool expected)
    {
        var response = CreateResponse(statusCode);

        Assert.Equal(expected, response.IsSuccessStatusCode);
    }

    [Fact]
    public void EnsureSuccess_NonSuccess_ThrowsTypedHttpExceptionWithResponseMetadata()
    {
        var response = new D365Response(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":\"bad\",\"message\":\"invalid\"}}",
            new Dictionary<string, string[]>(),
            RequestUri,
            "request-2",
            D365MutationOutcome.Rejected);

        var exception = Assert.Throws<D365HttpException>(response.EnsureSuccessStatusCode);

        Assert.Equal(D365FailureKind.Http, exception.FailureKind);
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(RequestUri, exception.RequestUri);
        Assert.Equal(response.RawBody, exception.ResponseBody);
        Assert.Equal("request-2", exception.RequestId);
        Assert.Equal(D365MutationOutcome.Rejected, exception.MutationOutcome);
    }

    [Fact]
    public void EnsureSuccess_Success_DoesNotThrow()
    {
        var response = CreateResponse(HttpStatusCode.NoContent);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void GenericResponse_PreservesTypedValueAndMetadata()
    {
        var response = new D365Response<TestEntity>(
            HttpStatusCode.Created,
            new TestEntity("created"),
            "{\"name\":\"created\"}",
            new Dictionary<string, string[]>(),
            RequestUri,
            "request-3",
            D365MutationOutcome.SucceededOrAccepted);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("created", response.Value?.Name);
        Assert.Equal(D365MutationOutcome.SucceededOrAccepted, response.MutationOutcome);
    }

    [Fact]
    public void OperationCanceledException_RemainsCatchableAsStandardCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = new D365OperationCanceledException(
            "D365 mutation was canceled after sending.",
            D365MutationOutcome.Unknown,
            cancellation.Token);

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(D365MutationOutcome.Unknown, exception.MutationOutcome);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static D365Response CreateResponse(HttpStatusCode statusCode)
    {
        return new D365Response(
            statusCode,
            string.Empty,
            new Dictionary<string, string[]>(),
            RequestUri,
            "request-1",
            statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                ? D365MutationOutcome.SucceededOrAccepted
                : D365MutationOutcome.Rejected);
    }

    private sealed record TestEntity(string Name);
}
