using System.Linq.Expressions;

namespace FlintsLabs.D365.ODataClient.Expressions;

/// <summary>
/// Replaces parameter expressions with constant values for Lambda evaluation
/// </summary>
internal class ParameterToConstantReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _parameter;
    private readonly object? _value;

    public ParameterToConstantReplacer(ParameterExpression parameter, object? value)
    {
        _parameter = parameter;
        _value = value;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node == _parameter)
        {
            return Expression.Constant(_value, node.Type);
        }
        return base.VisitParameter(node);
    }
}
