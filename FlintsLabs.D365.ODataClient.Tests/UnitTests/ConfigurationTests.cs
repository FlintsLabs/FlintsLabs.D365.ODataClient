using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Enums;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class ConfigurationTests
{
    [Fact]
    public void FromConfiguration_ShouldReadBooleanFormatting_Literal()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"D365:ClientId", "client-id"},
            {"D365:ClientSecret", "client-secret"},
            {"D365:TenantId", "tenant-id"},
            {"D365:Resource", "https://resource.com"},
            {"D365:BooleanFormatting", "Literal"}
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        var builder = new D365ClientBuilder();

        // Act
        builder.FromConfiguration(configuration, "D365");

        // Assert
        Assert.Equal(D365BooleanFormatting.Literal, builder.Options.BooleanFormatting);
    }

    [Fact]
    public void FromConfiguration_ShouldDefaultToNoYes_WhenMissing()
    {
        // Arrange
        var inMemorySettings = new Dictionary<string, string> {
            {"D365:ClientId", "client-id"},
            {"D365:ClientSecret", "client-secret"},
            {"D365:TenantId", "tenant-id"},
            {"D365:Resource", "https://resource.com"}
            // Missing BooleanFormatting
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();

        var builder = new D365ClientBuilder();

        // Act
        builder.FromConfiguration(configuration, "D365");

        // Assert
        Assert.Equal(D365BooleanFormatting.NoYesEnum, builder.Options.BooleanFormatting);
    }
}
