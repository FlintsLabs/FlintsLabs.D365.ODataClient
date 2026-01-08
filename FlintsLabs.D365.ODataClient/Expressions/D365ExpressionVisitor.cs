using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FlintsLabs.D365.ODataClient.Enums;

namespace FlintsLabs.D365.ODataClient.Expressions;

/// <summary>
/// Translates LINQ expressions to OData $filter queries
/// </summary>
public class D365ExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Translate expression to OData filter string
    /// </summary>
    public string Translate(Expression expression)
    {
        Visit(expression);
        return _sb.ToString();
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
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
        Visit(node.Operand);
        return node;
    }

    private void AppendConstant(object? value)
    {
        if (value == null)
        {
            _sb.Append("null");
            return;
        }

        switch (value)
        {
            // String
            case string s:
                // Special case for DataEntity Enum e.g. Microsoft.Dynamics.DataEntities.EcoResProductType'Item'
                if (s.StartsWith("Microsoft.Dynamics.DataEntities.") ||
                    s.StartsWith("Microsoft.Dynamics.AX.Application."))
                {
                    _sb.Append(s); // Don't add single quotes around it
                    break;
                }

                _sb.Append('\'').Append(s.Replace("'", "''")).Append('\'');
                break;

            // Boolean (D365 NoYes enum)
            case bool b:
                _sb.Append(b ? D365NoYes.Yes : D365NoYes.No);
                break;

            // DateTime -> ISO8601 UTC
            case DateTime dt:
                _sb.Append(dt.ToUniversalTime().ToString("s") + "Z");
                break;

            // Enum -> string literal
            case Enum e:
                _sb.Append('\'').Append(e.ToString()).Append('\'');
                break;

            // Numeric types
            case int or long or double or decimal:
                _sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;

            // Fallback
            default:
                _sb.Append("'").Append(value.ToString()?.Replace("'", "''")).Append("'");
                break;
        }
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
        catch (Exception ex)
        {
            Console.WriteLine($"[GetValue ERROR] {member.Member.Name}: {ex.Message}");
            return null;
        }
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // Handle List<T>.Contains(x.Property) or Enumerable.Contains(list, x.Property)
        // Generates: (property eq 'val1' or property eq 'val2' or ...)
        if (node.Method.Name == "Contains")
        {
            // Case 1: Instance method - list.Contains(x.Property)
            if (node.Object != null && node.Arguments.Count == 1)
            {
                var listValue = GetValueFromExpression(node.Object);
                var propertyExpr = node.Arguments[0] as MemberExpression;
                
                if (listValue is System.Collections.IEnumerable enumerable && propertyExpr != null)
                {
                    return BuildInClauseFilter(enumerable, propertyExpr);
                }
            }
            
            // Case 2: Extension method - Enumerable.Contains(list, x.Property)
            if (node.Object == null && node.Arguments.Count == 2)
            {
                var listValue = GetValueFromExpression(node.Arguments[0]);
                var propertyExpr = node.Arguments[1] as MemberExpression;
                
                if (listValue is System.Collections.IEnumerable enumerable && propertyExpr != null)
                {
                    return BuildInClauseFilter(enumerable, propertyExpr);
                }
            }
        }
        
        // Handle DateTime and other .NET method calls by evaluating the value
        try
        {
            var instance = node.Object is MemberExpression objMember ? GetValue(objMember) : null;
            var args = node.Arguments.Select(GetValueFromExpression).ToArray();

            var result = node.Method.Invoke(instance, args);
            AppendConstant(result);
        }
        catch
        {
            // Handle known OData functions for string
            if (node.Method.DeclaringType == typeof(string))
            {
                var arg = GetValueFromExpression(node.Arguments[0]);
                var propName = (node.Object as MemberExpression)?.Member.GetCustomAttribute<JsonPropertyNameAttribute>()
                               ?.Name
                               ?? (node.Object as MemberExpression)?.Member.Name ?? "unknown";

                switch (node.Method.Name)
                {
                    case nameof(string.Contains):
                        _sb.Append($"contains({propName},'{arg}')");
                        return node;
                    case nameof(string.StartsWith):
                        _sb.Append($"startswith({propName},'{arg}')");
                        return node;
                    case nameof(string.EndsWith):
                        _sb.Append($"endswith({propName},'{arg}')");
                        return node;
                }
            }

            _sb.Append("null"); // fallback
        }

        return node;
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
            
            // Format value based on type
            string formatted = val switch
            {
                string s => $"'{s}'",
                Guid g => g.ToString(),
                int or long or short or byte => val.ToString()!,
                decimal or double or float => Convert.ToString(val, CultureInfo.InvariantCulture)!,
                _ => $"'{val}'"
            };
            
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

    private static object? EvaluateMethodCall(MethodCallExpression mc)
    {
        var instance = mc.Object is MemberExpression objMember ? GetValue(objMember) : null;
        var args = mc.Arguments.Select(GetValueFromExpression).ToArray();
        try
        {
            return mc.Method.Invoke(instance, args);
        }
        catch
        {
            return null;
        }
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
