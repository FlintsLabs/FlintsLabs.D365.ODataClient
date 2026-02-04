using System.Net;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Attributes;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class KeyWhereUpdateTests
{
    private class EgrHeadEth
    {
        [OdataKey]
        [JsonPropertyName("rvl_egrheadethid")]
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task UpdateAsync_WithWhereKey_ShouldBuildPatchUrl()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NoContent,
                Content = new StringContent(string.Empty)
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://org782e707f.api.crm5.dynamics.com/api/data/v9.2/")
        };

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var tokenProviderMock = new Mock<ID365AccessTokenProvider>();
        tokenProviderMock.Setup(x => x.GetAccessTokenAsync()).ReturnsAsync("fake-token");

        var options = new D365ClientOptions
        {
            OrganizationUrl = "https://org782e707f.api.crm5.dynamics.com/api/data/v9.2/"
        };

        var query = new D365Query<EgrHeadEth>(
            httpClientFactoryMock.Object,
            Mock.Of<ILogger>(),
            tokenProviderMock.Object,
            "rvl_egrheadeths",
            options
        );

        var id = new Guid("20136305-68d1-ef11-8ee9-000d3aa08849");

        // Act
        await query
            .Where(x => x.Id == id)
            .UpdateAsync(new { rvl_wmsstatus = false });

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Equal(
            "https://org782e707f.api.crm5.dynamics.com/api/data/v9.2/rvl_egrheadeths(rvl_egrheadethid=20136305-68d1-ef11-8ee9-000d3aa08849)",
            captured.RequestUri!.ToString());

        var body = await captured.Content!.ReadAsStringAsync();
        Assert.Equal("{\"rvl_wmsstatus\":false}", body);
    }
}
