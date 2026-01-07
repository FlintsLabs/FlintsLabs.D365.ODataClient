using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;

namespace FlintsLabs.D365.ODataClient.Expressions;

/// <summary>
/// Helper class for extracting property names from LINQ expressions
/// </summary>
public static class D365ExpressionHelper
{
    /// <summary>
    /// Get property names from expression for $select
    /// </summary>
    public static string[] GetPropertyNamesFromExpression(Type entityType, LambdaExpression expression)
    {
        if (expression.Body is NewExpression newExpr)
        {
            var names = new List<string>();

            // Use Arguments to find source property name
            for (int i = 0; i < newExpr.Arguments.Count; i++)
            {
                var arg = newExpr.Arguments[i];
                if (arg is MemberExpression m)
                {
                    names.Add(GetJsonName(m.Member));
                }
                else if (arg is UnaryExpression u && u.Operand is MemberExpression um)
                {
                    names.Add(GetJsonName(um.Member));
                }
                else
                {
                    // Fallback (single property case)
                    names.Add(newExpr.Members![i].Name);
                }
            }

            return names.ToArray();
        }

        if (expression.Body is MemberInitExpression initExpr)
            return initExpr.Bindings.Select(b => GetJsonName(b.Member)).ToArray();

        var body = expression.Body is UnaryExpression un ? un.Operand : expression.Body;
        if (body is MemberExpression single)
            return new[] { GetJsonName(single.Member) };

        throw new NotSupportedException($"Unsupported expression type: {expression.Body.NodeType}");
    }

    /// <summary>
    /// Get single property name for navigation (in Expand)
    /// </summary>
    public static string GetPropertyName(LambdaExpression expr)
    {
        var body = expr.Body is UnaryExpression u ? u.Operand : expr.Body;
        if (body is MemberExpression m)
            return GetJsonName(m.Member);

        throw new InvalidOperationException("Invalid navigation property expression.");
    }

    /// <summary>
    /// Get JSON property name or default property name
    /// </summary>
    private static string GetJsonName(MemberInfo member)
    {
        var attr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? member.Name;
    }

    /// <summary>
    /// Validate expression (for debugging)
    /// </summary>
    public static void ValidateExpression(LambdaExpression expr)
    {
        if (expr.Body is not (NewExpression or MemberInitExpression or MemberExpression or UnaryExpression))
            throw new InvalidOperationException($"Unsupported expression type: {expr.Body.NodeType}");
    }
}
