using System.Net;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class HeaderTests
{
    private class TestEntity
    {
        public string? Name { get; set; }
    }

    [Fact]
    public async Task AddHeader_ShouldAddHeaderToRequest()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"value\": []}")
        };

        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(response);

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://example.com/data/")
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger>();
        var tokenProvider = new StubTokenProvider("fake-token");

        var options = new D365ClientOptions { Resource = "https://example.com" };

        var query = D365QueryTestFactory.Create<TestEntity>(
            httpClientFactoryMock.Object,
            loggerMock.Object,
            tokenProvider,
            "TestEntities",
            options
        );

        // Act
        var headerValue = "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"";
        query.AddHeader("Prefer", headerValue);
        await query.ToListAsync();

        // Assert
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.Headers.Contains("Prefer") &&
              req.Headers.GetValues("Prefer").First() == headerValue
           ),
           ItExpr.IsAny<CancellationToken>()
        );

        loggerMock.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Never,
            "Expected no errors to be logged"
        );
    }
}
