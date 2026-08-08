using FlintsLabs.D365.ODataClient.Extensions;
using FlintsLabs.D365.ODataClient.Enums;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class ConfigurationTests
{
    [Fact]
    public void UseSystemAssignedManagedIdentity_SelectsManagedIdentityWithoutClientId()
    {
        var builder = new D365ClientBuilder();

        builder
            .UseUserAssignedManagedIdentity("11111111-1111-1111-1111-111111111111")
            .UseSystemAssignedManagedIdentity();

        Assert.Equal(D365AuthType.ManagedIdentity, builder.Options.AuthType);
        Assert.Null(builder.Options.ManagedIdentityClientId);
    }

    [Fact]
    public void UseUserAssignedManagedIdentity_SelectsManagedIdentityWithClientId()
    {
        const string clientId = "11111111-1111-1111-1111-111111111111";
        var builder = new D365ClientBuilder();

        builder
            .UseSystemAssignedManagedIdentity()
            .UseUserAssignedManagedIdentity(clientId);

        Assert.Equal(D365AuthType.ManagedIdentity, builder.Options.AuthType);
        Assert.Equal(clientId, builder.Options.ManagedIdentityClientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    public void UseUserAssignedManagedIdentity_RejectsInvalidClientId(string? clientId)
    {
        var builder = new D365ClientBuilder();

        Assert.ThrowsAny<ArgumentException>(() =>
            builder.UseUserAssignedManagedIdentity(clientId!));
    }

    [Fact]
    public void ExistingAuthenticationSelectors_RemainAvailable()
    {
        var builder = new D365ClientBuilder();

        builder
            .UseUserAssignedManagedIdentity("11111111-1111-1111-1111-111111111111")
            .UseAzureAD();
        Assert.Equal(D365AuthType.AzureAD, builder.Options.AuthType);
        Assert.Null(builder.Options.ManagedIdentityClientId);

        builder
            .UseUserAssignedManagedIdentity("22222222-2222-2222-2222-222222222222")
            .UseADFS();
        Assert.Equal(D365AuthType.ADFS, builder.Options.AuthType);
        Assert.Null(builder.Options.ManagedIdentityClientId);
    }

    [Fact]
    public void FromConfiguration_RemainsClientSecretAuthenticationUnlessAdfsIsDetected()
    {
        var settings = new Dictionary<string, string>
        {
            ["D365:ClientId"] = "client-id",
            ["D365:ClientSecret"] = "client-secret",
            ["D365:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["D365:Resource"] = "https://resource.example.test"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings!)
            .Build();
        var builder = new D365ClientBuilder();

        builder
            .UseUserAssignedManagedIdentity("22222222-2222-2222-2222-222222222222")
            .FromConfiguration(configuration);

        Assert.Equal(D365AuthType.AzureAD, builder.Options.AuthType);
        Assert.Null(builder.Options.ManagedIdentityClientId);
    }

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
