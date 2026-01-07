using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.Tests.Fixtures;

namespace FlintsLabs.D365.ODataClient.Tests.IntegrationTests;

public class ConnectivityTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task Cloud_CanConnect_And_CountLegalEntities()
    {
        // Arrange
        var d365 = GetService(D365ServiceScope.Cloud);
        if (d365 == null) throw new SkipException("Cloud config not found.");

        // Act
        var count = await d365.Entity<dynamic>("LegalEntities")
            .CrossCompany()
            .CountAsync();

        // Assert
        Assert.True(count > 0, "Should retrieve at least one legal entity from Cloud.");
    }

    [SkippableFact]
    public async Task OnPrem_CanConnect_And_CountLegalEntities()
    {
        // Arrange
        var d365 = GetService(D365ServiceScope.OnPrem);
        if (d365 == null) throw new SkipException("OnPrem config not found.");

        // Act
        var count = await d365.Entity<dynamic>("LegalEntities")
            .CrossCompany()
            .CountAsync();

        // Assert
        Assert.True(count > 0, "Should retrieve at least one legal entity from OnPrem.");
    }
}
