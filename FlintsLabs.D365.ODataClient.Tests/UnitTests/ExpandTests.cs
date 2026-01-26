using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class ExpandTests
{
    private class TestEntity
    {
        public string? Name { get; set; }
        public TestEntity? Parent { get; set; }
    }

    [Fact]
    public async Task Expand_String_ShouldAddToQuery()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent("{\"value\": []}")
           });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://example.com/data/")
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger>();
        var tokenProviderMock = new Mock<ID365AccessTokenProvider>();
        tokenProviderMock.Setup(x => x.GetAccessTokenAsync()).ReturnsAsync("fake-token");

        var options = new D365ClientOptions { Resource = "https://example.com" };

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            loggerMock.Object,
            tokenProviderMock.Object,
            "TestEntities",
            options
        );

        // Act
        query.Expand("EgrLines");
        await query.ToListAsync();

        // Assert
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.RequestUri!.ToString().Contains("$expand=EgrLines")
           ),
           ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task Expand_Expression_ShouldAddToQuery()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent("{\"value\": []}")
           });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://example.com/data/")
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger>();
        var tokenProviderMock = new Mock<ID365AccessTokenProvider>();
        tokenProviderMock.Setup(x => x.GetAccessTokenAsync()).ReturnsAsync("fake-token");

        var options = new D365ClientOptions { Resource = "https://example.com" };

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            loggerMock.Object,
            tokenProviderMock.Object,
            "TestEntities",
            options
        );

        // Act
        query.Expand(x => x.Parent!);
        await query.ToListAsync();

        // Assert
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.RequestUri!.ToString().Contains("$expand=Parent")
           ),
           ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task Expand_SelectAll_ShouldWork()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
              "SendAsync",
              ItExpr.IsAny<HttpRequestMessage>(),
              ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent("{\"value\": []}")
           });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://example.com/data/")
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var loggerMock = new Mock<ILogger>();
        var tokenProviderMock = new Mock<ID365AccessTokenProvider>();
        tokenProviderMock.Setup(x => x.GetAccessTokenAsync()).ReturnsAsync("fake-token");

        var options = new D365ClientOptions { Resource = "https://example.com" };

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            loggerMock.Object,
            tokenProviderMock.Object,
            "TestEntities",
            options
        );

        // Act & Assert
        // Reproducing Issue #1: query.Expand("Parent", x => x);
        // We expect this to either work (expand without select) or throw a nice error.
        // Currently expecting it to fail with NotSupportedException.
        
        // Using "await" to ensure async flow is respected if we were doing real calls, though Expand is synchronous builder.
        // But since we call ToListAsync to trigger URL built...
        
        query.Expand<TestEntity>("Parent", x => x);
        
        await query.ToListAsync();
        
        // If it works, it should probably be just $expand=Parent without select, or select=* 
        // But OData usually defaults to select * if no select is present.
        
        handlerMock.Protected().Verify(
           "SendAsync",
           Times.Once(),
           ItExpr.Is<HttpRequestMessage>(req =>
              req.RequestUri!.ToString().Contains("$expand=Parent($select=*)")
           ),
           ItExpr.IsAny<CancellationToken>()
        );
    }
}
