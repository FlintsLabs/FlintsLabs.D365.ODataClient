using System.Linq.Expressions;
using System.Net;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Expressions;
using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.OData;
using FlintsLabs.D365.ODataClient.Services;
using FlintsLabs.D365.ODataClient.Tests.TestInfrastructure;
using FlintsLabs.D365.ODataClient.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests.OData;

public class ODataQueryEncodingTests
{
    [Fact]
    public async Task Query_EncodesReservedCharactersWithoutChangingODataLiteral()
    {
        var transport = new CapturingTransport();
        var options = CreateOptions();
        options.BooleanFormatting = D365BooleanFormatting.Literal;
        var query = CreateQuery(transport, options);
        var id = Guid.Parse("20136305-68d1-ef11-8ee9-000d3aa08849");
        var createdOn = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        const string name = "O'Brien";
        const string dimension = "DIM#001&A+B%20 ไทย";

        await query
            .CrossCompany()
            .Where(entity =>
                entity.Name == name
                && entity.Dimension == dimension
                && entity.Id == id
                && entity.CreatedOn >= createdOn
                && entity.Active == true
                && entity.Company == "TH01")
            .Select(entity => new { entity.Name, entity.Company })
            .OrderBy(entity => entity.Name)
            .Skip(3)
            .ToListAsync();

        var requestUrl = Assert.Single(transport.Requests).RelativeOrAbsoluteUrl;
        var absoluteUri = new Uri(new Uri("https://example.test/data/"), requestUrl);
        var decodedQuery = Uri.UnescapeDataString(absoluteUri.Query);

        Assert.Empty(absoluteUri.Fragment);
        Assert.Contains("cross-company=true", decodedQuery);
        Assert.Contains("Name eq 'O''Brien'", decodedQuery);
        Assert.Contains("Dimension eq 'DIM#001&A+B%20 ไทย'", decodedQuery);
        Assert.Contains($"Id eq {id}", decodedQuery);
        Assert.Contains("CreatedOn ge 2026-01-02T03:04:05.0000000Z", decodedQuery);
        Assert.Contains("Active eq true", decodedQuery);
        Assert.Contains("dataAreaId eq 'TH01'", decodedQuery);
        Assert.Contains("$select=Name,dataAreaId", decodedQuery);
        Assert.Contains("$orderby=Name asc", decodedQuery);
        Assert.Contains("$skip=3", decodedQuery);
        Assert.Contains("%23", absoluteUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%26", absoluteUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%2B", absoluteUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%25", absoluteUri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiteralFormatter_FormatsSupportedTypesDeterministically()
    {
        var id = Guid.Parse("20136305-68d1-ef11-8ee9-000d3aa08849");
        var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(7));

        Assert.Equal("null", ODataLiteralFormatter.Format(null));
        Assert.Equal("'O''Brien'", ODataLiteralFormatter.Format("O'Brien"));
        Assert.Equal(id.ToString(), ODataLiteralFormatter.Format(id));
        Assert.Equal("12.5", ODataLiteralFormatter.Format(12.5m));
        Assert.Equal("'Friday'", ODataLiteralFormatter.Format(DayOfWeek.Friday));
        Assert.Equal(
            "Microsoft.Dynamics.DataEntities.EcoResProductType'Item'",
            ODataLiteralFormatter.Format(
                "Microsoft.Dynamics.DataEntities.EcoResProductType'Item'"));
        Assert.Equal(
            "2026-01-01T20:04:05.0000000Z",
            ODataLiteralFormatter.Format(timestamp));
        Assert.Equal(
            "Microsoft.Dynamics.DataEntities.NoYes'Yes'",
            ODataLiteralFormatter.Format(true, D365BooleanFormatting.NoYesEnum));
        Assert.Equal("false", ODataLiteralFormatter.Format(false, D365BooleanFormatting.Literal));
    }

    [Fact]
    public void ExpressionVisitor_ListContainsEscapesStringLiterals()
    {
        var values = new[] { "O'Brien", "A&B" };
        Expression<Func<TestEntity, bool>> expression = entity => values.Contains(entity.Name);

        var translated = new D365ExpressionVisitor().Translate(expression.Body);

        Assert.Equal("(Name eq 'O''Brien' or Name eq 'A&B')", translated);
    }

    [Fact]
    public void ExpressionVisitor_UnsupportedMethodFailsWithClearMessage()
    {
        Expression<Func<TestEntity, bool>> expression = entity => entity.Name.ToLower() == "value";

        var exception = Assert.Throws<NotSupportedException>(() =>
            new D365ExpressionVisitor().Translate(expression.Body));

        Assert.Contains("ToLower", exception.Message);
        Assert.Contains("OData", exception.Message);
    }

    private static D365Query<TestEntity> CreateQuery(
        ID365Transport transport,
        D365ClientOptions options)
    {
        return D365QueryTestFactory.Create<TestEntity>(
            Mock.Of<IHttpClientFactory>(),
            NullLogger.Instance,
            new StubTokenProvider("token"),
            "Entities",
            options,
            transport);
    }

    private static D365ClientOptions CreateOptions()
    {
        return new D365ClientOptions
        {
            OrganizationUrl = "https://example.test/data/"
        };
    }

    private sealed class TestEntity
    {
        public string Name { get; set; } = string.Empty;
        public string Dimension { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public bool Active { get; set; }

        [JsonPropertyName("dataAreaId")]
        public string Company { get; set; } = string.Empty;
    }

    private sealed class CapturingTransport : ID365Transport
    {
        public List<D365Request> Requests { get; } = [];

        public Task<D365Response> SendRawAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            return SendEnsuredAsync(request, cancellationToken);
        }

        public Task<D365Response> SendEnsuredAsync(
            D365Request request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new D365Response(
                HttpStatusCode.OK,
                "{\"value\":[]}",
                new Dictionary<string, string[]>(),
                new Uri(new Uri("https://example.test/data/"), request.RelativeOrAbsoluteUrl),
                "request-query",
                D365MutationOutcome.NotApplicable));
        }
    }
}
