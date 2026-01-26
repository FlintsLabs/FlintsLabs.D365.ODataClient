using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Enums;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class BooleanTests
{
    private class TestEntity
    {
        public bool IsActive { get; set; }
    }

    [Fact]
    public async Task Where_Bool_ShouldUseNoYes_ByDefault()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
           .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
           .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{\"value\": []}") });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://example.com/data/") };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var options = new D365ClientOptions { Resource = "https://example.com" }; // Default config

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            Mock.Of<ILogger>(),
            Mock.Of<ID365AccessTokenProvider>(),
            "TestEntities",
            options
        );

        // Act
        query.Where(x => x.IsActive == false);
        await query.ToListAsync();

        // Assert
        // Current behavior: Microsoft.Dynamics.DataEntities.NoYes'No'
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.RequestUri!.ToString().Contains("IsActive eq Microsoft.Dynamics.DataEntities.NoYes'No'")
           ),
           ItExpr.IsAny<CancellationToken>()
        );
    }
    
    [Fact]
    public async Task Where_Bool_ShouldUseLiteral_WhenConfigured()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
           .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
           .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent("{\"value\": []}") });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("https://example.com/data/") };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var options = new D365ClientOptions 
        { 
            Resource = "https://example.com",
            BooleanFormatting = D365BooleanFormatting.Literal 
        };

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            Mock.Of<ILogger>(),
            Mock.Of<ID365AccessTokenProvider>(),
            "TestEntities",
            options
        );

        // Act
        query.Where(x => x.IsActive == false);
        await query.ToListAsync();

        // Assert
        // Expected behavior: IsActive eq false
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.RequestUri!.ToString().Contains("IsActive eq false")
           ),
           ItExpr.IsAny<CancellationToken>()
        );
    }
}
