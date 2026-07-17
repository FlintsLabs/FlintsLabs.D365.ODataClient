using System.Reflection;
using FlintsLabs.D365.ODataClient.Models;
using FlintsLabs.D365.ODataClient.Services;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class PublicApiRemovalTests
{
    private static readonly Assembly ClientAssembly = typeof(ID365Client).Assembly;

    [Theory]
    [InlineData("FlintsLabs.D365.ODataClient.Services.D365Service")]
    [InlineData("FlintsLabs.D365.ODataClient.Services.ID365Service")]
    [InlineData("FlintsLabs.D365.ODataClient.Services.D365ServiceFactory")]
    [InlineData("FlintsLabs.D365.ODataClient.Services.ID365ServiceFactory")]
    public void Version1ServiceTypesAreAbsent(string fullName)
    {
        Assert.Null(ClientAssembly.GetType(fullName, throwOnError: false));
    }

    [Fact]
    public void QueryCanOnlyBeCreatedByTheClientPipeline()
    {
        var publicConstructors = typeof(D365Query<>).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void EveryPublicAsyncMethodAcceptsOptionalCancellationToken()
    {
        var asyncMethods = ClientAssembly.ExportedTypes
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            .Where(method => IsTaskLike(method.ReturnType))
            .ToArray();

        Assert.NotEmpty(asyncMethods);
        foreach (var method in asyncMethods)
        {
            var cancellationToken = Assert.Single(
                method.GetParameters(),
                parameter => parameter.ParameterType == typeof(CancellationToken));
            Assert.True(
                cancellationToken.HasDefaultValue,
                $"{method.DeclaringType?.FullName}.{method.Name} must make CancellationToken optional.");
        }
    }

    [Fact]
    public void EntityMethodsAreGenericOnly()
    {
        var entityMethods = typeof(ID365Client).GetMethods()
            .Where(method => method.Name == nameof(ID365Client.Entity))
            .ToArray();

        Assert.NotEmpty(entityMethods);
        Assert.All(entityMethods, method => Assert.True(method.IsGenericMethodDefinition));
    }

    [Fact]
    public void MutationMethodsReturnResponseContractsInsteadOfStringsOrDefaults()
    {
        var mutationMethods = typeof(D365Query<>).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is "AddAsync" or "UpdateAsync" or "DeleteAsync")
            .ToArray();

        Assert.NotEmpty(mutationMethods);
        foreach (var method in mutationMethods)
        {
            Assert.True(method.ReturnType.IsGenericType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());
            var resultType = method.ReturnType.GetGenericArguments()[0];
            Assert.True(
                resultType == typeof(D365Response)
                || resultType.IsGenericType
                && resultType.GetGenericTypeDefinition() == typeof(D365Response<>),
                $"Unexpected mutation return type: {method}");
        }
    }

    private static bool IsTaskLike(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
            return true;
        if (!returnType.IsGenericType)
            return false;

        var definition = returnType.GetGenericTypeDefinition();
        return definition == typeof(Task<>) || definition == typeof(ValueTask<>);
    }
}
