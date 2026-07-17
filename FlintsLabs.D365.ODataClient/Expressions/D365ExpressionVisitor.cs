using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Enums;
using FlintsLabs.D365.ODataClient.OData;

namespace FlintsLabs.D365.ODataClient.Expressions;

/// <summary>
/// Translates LINQ expressions to OData $filter queries
/// </summary>
public class D365ExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();
    private readonly D365BooleanFormatting _booleanFormatting;

    public D365ExpressionVisitor(D365BooleanFormatting booleanFormatting = D365BooleanFormatting.NoYesEnum)
    {
        _booleanFormatting = booleanFormatting;
    }

    /// <summary>
    /// Translate expression to OData filter string
    /// </summary>
    public string Translate(Expression expression)
    {
        try
        {
            Visit(expression);
            return _sb.ToString();
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(
                $"Failed to translate LINQ expression to OData filter. {ex.Message} Expression: {expression}",
                ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to translate LINQ expression to OData filter. Expression: {expression}",
                ex);
        }
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.Coalesce)
        {
            return VisitCoalesce(node);
        }

        // Check if it's a logical operator (and/or)
        bool isLogical = node.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse;

        // Check if left or right is logical expression
        bool leftIsLogical = node.Left is BinaryExpression leftBinary &&
                             (leftBinary.NodeType == ExpressionType.AndAlso || leftBinary.NodeType == ExpressionType.OrElse);

        bool rightIsLogical = node.Right is BinaryExpression rightBinary &&
                              (rightBinary.NodeType == ExpressionType.AndAlso ||
                               rightBinary.NodeType == ExpressionType.OrElse);

        // Add parentheses if this is not root expression and is a logic expression
        bool needParen = isLogical && (leftIsLogical || rightIsLogical);

        if (needParen)
            _sb.Append("(");

        Visit(node.Left);

        _sb.Append(" ").Append(GetOperator(node.NodeType)).Append(" ");

        Visit(node.Right);

        if (needParen)
            _sb.Append(")");

        return node;
    }

    /// <summary>
    /// Translate null-coalescing operator (??) to OData coalesce(left,right)
    /// </summary>
    private Expression VisitCoalesce(BinaryExpression node)
    {
        if (node.Conversion != null)
        {
            throw new NotSupportedException(
                "Coalesce with conversion is not supported. Use a simple null-coalescing expression without conversion.");
        }

        _sb.Append("coalesce(");
        Visit(node.Left);
        _sb.Append(",");
        Visit(node.Right);
        _sb.Append(")");
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ConstantExpression constExpr)
        {
            var container = constExpr.Value;
            if (node.Member is FieldInfo field)
            {
                var value = field.GetValue(container);
                AppendConstant(value);
                return node;
            }
        }

        if (node.Expression is MemberExpression parentMember &&
            parentMember.Expression is ConstantExpression parentConst)
        {
            var container = parentConst.Value;
            var parentValue = ((FieldInfo)parentMember.Member).GetValue(container);
            var childValue = node.Member switch
            {
                PropertyInfo p => p.GetValue(parentValue),
                FieldInfo f => f.GetValue(parentValue),
                _ => null
            };
            AppendConstant(childValue);
            return node;
        }

        // If it's a property of object T -> use property name (or JsonPropertyName)
        if (node.Expression is ParameterExpression)
        {
            var jsonAttr = node.Member.GetCustomAttribute<JsonPropertyNameAttribute>();
            var name = jsonAttr?.Name ?? node.Member.Name;
            _sb.Append(name);
            return node;
        }

        // Static member (e.g. DateTime.Today)
        var staticValue = GetValue(node);
        AppendConstant(staticValue);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AppendConstant(node.Value);
        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _sb.Append("not (");
            Visit(node.Operand);
            _sb.Append(")");
            return node;
        }

        Visit(node.Operand);
        return node;
    }

    private void AppendConstant(object? value)
    {
        _sb.Append(ODataLiteralFormatter.Format(value, _booleanFormatting));
    }

    private static object? GetValue(MemberExpression member)
    {
        try
        {
            if (member.Member is FieldInfo field)
            {
                var target = member.Expression switch
                {
                    MemberExpression inner => GetValue(inner),
                    ConstantExpression constExpr => constExpr.Value,
                    _ => null
                };

                return field.IsStatic ? field.GetValue(null) : field.GetValue(target);
            }

            if (member.Member is PropertyInfo prop)
            {
                var target = member.Expression switch
                {
                    MemberExpression inner => GetValue(inner),
                    ConstantExpression constExpr => constExpr.Value,
                    _ => null
                };

                if (prop.GetMethod?.IsStatic == true)
                    return prop.GetValue(null);

                // If property is from struct (e.g. DateTime.Date) and target == null -> evaluate expression instead
                if (target == null && prop.DeclaringType?.IsValueType == true)
                {
                    var lambda = Expression.Lambda(member);
                    return lambda.Compile().DynamicInvoke();
                }

                return prop.GetValue(target);
            }

            return null;
        }
        catch (Exception exception)
        {
            throw new NotSupportedException(
                $"Member '{member.Member.Name}' could not be evaluated as an OData constant.",
                exception);
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Handle List<T>.Contains(x.Property) or Enumerable.Contains(list, x.Property)
        // Generates: (property eq 'val1' or property eq 'val2' or ...)
        if (node.Method.Name == "Contains")
        {
            // Case 1: Instance method - list.Contains(x.Property)
            if (node.Object != null
                && node.Object.Type != typeof(string)
                && node.Arguments.Count == 1)
            {
                var listValue = GetCollectionValue(node.Object);
                var propertyExpr = node.Arguments[0] as MemberExpression;
                
                if (listValue is System.Collections.IEnumerable enumerable && propertyExpr != null)
                {
                    return BuildInClauseFilter(enumerable, propertyExpr);
                }
            }
            
            // Case 2: Extension method - Enumerable.Contains(list, x.Property)
            if (node.Object == null && node.Arguments.Count == 2)
            {
                var listValue = GetCollectionValue(node.Arguments[0]);
                var propertyExpr = node.Arguments[1] as MemberExpression;
                
                if (listValue is System.Collections.IEnumerable enumerable && propertyExpr != null)
                {
                    return BuildInClauseFilter(enumerable, propertyExpr);
                }
            }
        }
        
        // Handle GetValueOrDefault() for nullable booleans.
        if (node.Method.Name == "GetValueOrDefault"
            && node.Object is MemberExpression nullableBoolean
            && (nullableBoolean.Type == typeof(bool?) || nullableBoolean.Type == typeof(bool)))
        {
            _sb.Append("(");
            Visit(nullableBoolean);
            _sb.Append(" eq ");
            AppendConstant(true);
            _sb.Append(")");
            return node;
        }

        if (node.Method.DeclaringType == typeof(string)
            && node.Object is not null
            && node.Arguments.Count == 1
            && node.Method.Name is nameof(string.Contains)
                or nameof(string.StartsWith)
                or nameof(string.EndsWith))
        {
            var functionName = node.Method.Name switch
            {
                nameof(string.Contains) => "contains",
                nameof(string.StartsWith) => "startswith",
                _ => "endswith"
            };
            _sb.Append(functionName).Append('(');
            Visit(node.Object);
            _sb.Append(',');
            AppendConstant(GetValueFromExpression(node.Arguments[0]));
            _sb.Append(')');
            return node;
        }

        try
        {
            AppendConstant(Expression.Lambda(node).Compile().DynamicInvoke());
            return node;
        }
        catch (Exception exception)
        {
            throw new NotSupportedException(
                $"Method '{node.Method.Name}' cannot be translated to OData.",
                exception);
        }
    }
    
    /// <summary>
    /// Build OData filter for IN clause: (property eq 'val1' or property eq 'val2' or ...)
    /// </summary>
    private Expression BuildInClauseFilter(System.Collections.IEnumerable values, MemberExpression propertyExpr)
    {
        var jsonAttr = propertyExpr.Member.GetCustomAttribute<JsonPropertyNameAttribute>();
        var propName = jsonAttr?.Name ?? propertyExpr.Member.Name;
        
        var orParts = new List<string>();
        foreach (var val in values)
        {
            if (val == null) continue;
            
            var formatted = ODataLiteralFormatter.Format(val, _booleanFormatting);
            orParts.Add($"{propName} eq {formatted}");
        }
        
        if (orParts.Count == 0)
        {
            _sb.Append("false"); // Empty list = always false
        }
        else if (orParts.Count == 1)
        {
            _sb.Append(orParts[0]);
        }
        else
        {
            _sb.Append($"({string.Join(" or ", orParts)})");
        }
        
        return propertyExpr;
    }

    private static object? GetValueFromExpression(Expression expr)
    {
        return expr switch
        {
            ConstantExpression c => c.Value,
            MemberExpression m => GetValue(m),
            MethodCallExpression mc => EvaluateMethodCall(mc),
            _ => Expression.Lambda(expr).Compile().DynamicInvoke()
        };
    }

    private static object? GetCollectionValue(Expression expression)
    {
        // .NET 10 can represent array Contains as an implicit ReadOnlySpan<T>
        // conversion. Evaluate the source collection because spans cannot be boxed.
        if (expression is MethodCallExpression
            {
                Method.Name: "op_Implicit",
                Arguments.Count: 1
            } conversion)
        {
            return GetValueFromExpression(conversion.Arguments[0]);
        }

        return GetValueFromExpression(expression);
    }

    private static object? EvaluateMethodCall(MethodCallExpression mc)
    {
        var instance = mc.Object is MemberExpression objMember ? GetValue(objMember) : null;
        var args = mc.Arguments.Select(GetValueFromExpression).ToArray();
        return mc.Method.Invoke(instance, args);
    }

    private static string GetOperator(ExpressionType type) => type switch
    {
        ExpressionType.Equal => "eq",
        ExpressionType.NotEqual => "ne",
        ExpressionType.GreaterThan => "gt",
        ExpressionType.GreaterThanOrEqual => "ge",
        ExpressionType.LessThan => "lt",
        ExpressionType.LessThanOrEqual => "le",
        ExpressionType.AndAlso => "and",
        ExpressionType.OrElse => "or",
        _ => throw new NotSupportedException($"Operator '{type}' is not supported.")
    };
}
