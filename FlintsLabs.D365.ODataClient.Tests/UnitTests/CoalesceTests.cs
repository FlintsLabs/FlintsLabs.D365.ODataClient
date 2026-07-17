using System.Linq.Expressions;
using System.Net;
using FlintsLabs.D365.ODataClient.Expressions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class CoalesceTests
{
    private class TestEntity
    {
        public string? CustomerName { get; set; }
        public int? Priority { get; set; }
    }

    [Fact]
    public void Coalesce_String_ShouldTranslateToODataCoalesceFunction()
    {
        // Arrange
        Expression<Func<TestEntity, bool>> expr = x => (x.CustomerName ?? "Unknown") == "ACME";

        // Act
        var visitor = new D365ExpressionVisitor();
        var result = visitor.Translate(expr.Body);

        // Assert
        Assert.Equal("coalesce(CustomerName,'Unknown') eq 'ACME'", result);
    }

    [Fact]
    public async Task Where_WithCoalesce_ShouldIncludeCoalesceInRequestUrl()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
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

        var query = new D365Query<TestEntity>(
            httpClientFactoryMock.Object,
            Mock.Of<ILogger>(),
            new StubTokenProvider("fake-token"),
            "TestEntities",
            new D365ClientOptions { Resource = "https://example.com" });

        // Act
        query.Where(x => (x.CustomerName ?? "Unknown") == "ACME");
        await query.ToListAsync();

        // Assert
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                Uri.UnescapeDataString(req.RequestUri!.ToString())
                    .Contains("coalesce(CustomerName,'Unknown') eq 'ACME'")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public void Coalesce_WithConversion_ShouldFailGracefullyWithClearError()
    {
        // Arrange
        var parameter = Expression.Parameter(typeof(TestEntity), "x");
        var left = Expression.Property(parameter, nameof(TestEntity.Priority)); // int?
        var right = Expression.Constant(0L, typeof(long));
        var conversionInput = Expression.Parameter(typeof(int), "v");
        var conversion = Expression.Lambda(Expression.Convert(conversionInput, typeof(long)), conversionInput);

        var coalesceWithConversion = Expression.Coalesce(left, right, conversion);
        var visitor = new D365ExpressionVisitor();

        // Act
        var ex = Assert.Throws<NotSupportedException>(() => visitor.Translate(coalesceWithConversion));

        // Assert
        Assert.Contains("Coalesce with conversion is not supported", ex.Message);
        Assert.Contains("Failed to translate LINQ expression to OData filter", ex.Message);
    }
}
