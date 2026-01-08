using System.Linq.Expressions;
using FlintsLabs.D365.ODataClient.Expressions;

namespace FlintsLabs.D365.ODataClient.Tests.UnitTests;

public class ListContainsTests
{
    public class TestEntity
    {
        public string ItemNumber { get; set; } = "";
        public int Quantity { get; set; }
    }
    
    [Fact]
    public void ListContains_String_GeneratesOrFilter()
    {
        // Arrange
        var items = new List<string> { "A001", "A002", "A003" };
        Expression<Func<TestEntity, bool>> expr = x => items.Contains(x.ItemNumber);
        
        // Act
        var visitor = new D365ExpressionVisitor();
        var result = visitor.Translate(expr.Body);
        
        // Assert
        Assert.Equal("(ItemNumber eq 'A001' or ItemNumber eq 'A002' or ItemNumber eq 'A003')", result);
    }
    
    [Fact]
    public void ListContains_SingleValue_OmitsParentheses()
    {
        // Arrange
        var items = new List<string> { "A001" };
        Expression<Func<TestEntity, bool>> expr = x => items.Contains(x.ItemNumber);
        
        // Act
        var visitor = new D365ExpressionVisitor();
        var result = visitor.Translate(expr.Body);
        
        // Assert
        Assert.Equal("ItemNumber eq 'A001'", result);
    }
    
    [Fact]
    public void ListContains_Integer_GeneratesOrFilter()
    {
        // Arrange
        var quantities = new List<int> { 10, 20, 30 };
        Expression<Func<TestEntity, bool>> expr = x => quantities.Contains(x.Quantity);
        
        // Act
        var visitor = new D365ExpressionVisitor();
        var result = visitor.Translate(expr.Body);
        
        // Assert
        Assert.Equal("(Quantity eq 10 or Quantity eq 20 or Quantity eq 30)", result);
    }
    
    [Fact]
    public void ListContains_EmptyList_ReturnsFalse()
    {
        // Arrange
        var items = new List<string>();
        Expression<Func<TestEntity, bool>> expr = x => items.Contains(x.ItemNumber);
        
        // Act
        var visitor = new D365ExpressionVisitor();
        var result = visitor.Translate(expr.Body);
        
        // Assert
        Assert.Equal("false", result);
    }
}
