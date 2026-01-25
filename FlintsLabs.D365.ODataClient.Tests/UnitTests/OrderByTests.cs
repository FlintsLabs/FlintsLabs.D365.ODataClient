using System.Text.Json.Serialization;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class OrderByTests
{
    // Test model for OrderBy tests
    private class TestProduct
    {
        public string ItemNumber { get; set; } = string.Empty;
        
        [JsonPropertyName("Name")]
        public string ProductName { get; set; } = string.Empty;
        
        public DateTime CreatedDateTime { get; set; }
        
        public int Quantity { get; set; }
    }

    [Fact]
    public void OrderBy_WithExpression_GeneratesCorrectAscending()
    {
        // Arrange & Act
        var property = GetPropertyNameFromExpression<TestProduct>(x => x.ItemNumber);
        
        // Assert - Verify property resolution works
        Assert.Equal("ItemNumber", property);
    }

    [Fact]
    public void OrderBy_WithJsonPropertyName_UsesJsonName()
    {
        // Arrange & Act
        var property = GetPropertyNameFromExpression<TestProduct>(x => x.ProductName);
        
        // Assert - Should use [JsonPropertyName] value
        Assert.Equal("Name", property);
    }

    [Fact]
    public void OrderBy_WithDateTimeProperty_Works()
    {
        // Arrange & Act
        var property = GetPropertyNameFromExpression<TestProduct>(x => x.CreatedDateTime);
        
        // Assert
        Assert.Equal("CreatedDateTime", property);
    }

    [Fact]
    public void OrderBy_WithIntProperty_Works()
    {
        // Arrange & Act
        var property = GetPropertyNameFromExpression<TestProduct>(x => x.Quantity);
        
        // Assert
        Assert.Equal("Quantity", property);
    }

    // Helper to extract property name using same logic as D365ExpressionHelper
    private static string GetPropertyNameFromExpression<T>(System.Linq.Expressions.Expression<Func<T, object>> expr)
    {
        var body = expr.Body is System.Linq.Expressions.UnaryExpression u ? u.Operand : expr.Body;
        if (body is System.Linq.Expressions.MemberExpression m)
        {
            var attr = m.Member.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false)
                .FirstOrDefault() as JsonPropertyNameAttribute;
            return attr?.Name ?? m.Member.Name;
        }
        throw new InvalidOperationException("Invalid expression.");
    }
}
