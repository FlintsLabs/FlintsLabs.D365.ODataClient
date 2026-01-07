using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlintsLabs.D365.ODataClient.Expressions;

/// <summary>
/// Evaluates LINQ expressions against JSON elements for client-side filtering
/// </summary>
public class JsonElementExpressionEvaluator
{
    private readonly JsonElement _rootElement;

    public JsonElementExpressionEvaluator(JsonElement rootElement)
    {
        _rootElement = rootElement;
    }

    /// <summary>
    /// Evaluate expression and return boolean result
    /// </summary>
    public bool Evaluate(Expression expression)
    {
        var result = Visit(expression);
        if (result is bool b) return b;
        throw new InvalidOperationException("Expression must return a boolean result.");
    }

    private object? Visit(Expression? node)
    {
        if (node == null) return null;

        switch (node.NodeType)
        {
            case ExpressionType.Lambda:
                return Visit(((LambdaExpression)node).Body);

            case ExpressionType.AndAlso:
            case ExpressionType.OrElse:
            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
                return VisitBinary((BinaryExpression)node);

            case ExpressionType.Not:
                return VisitUnary((UnaryExpression)node);

            case ExpressionType.Call:
                return VisitMethodCall((MethodCallExpression)node);

            case ExpressionType.MemberAccess:
                return VisitMember((MemberExpression)node);

            case ExpressionType.Constant:
                return ((ConstantExpression)node).Value;

            case ExpressionType.Convert:
                return Visit(((UnaryExpression)node).Operand);
        }

        throw new NotSupportedException($"Unsupported expression type: {node.NodeType}");
    }

    private object? VisitUnary(UnaryExpression node)
    {
        var operand = Visit(node.Operand);
        if (node.NodeType == ExpressionType.Not)
        {
            if (operand is bool b) return !b;
            throw new InvalidOperationException("NOT operator requires boolean operand.");
        }
        return operand;
    }

    private object? VisitBinary(BinaryExpression node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);

        try
        {
            return node.NodeType switch
            {
                ExpressionType.AndAlso => ConvertToBool(left) && ConvertToBool(right),
                ExpressionType.OrElse => ConvertToBool(left) || ConvertToBool(right),
                ExpressionType.Equal => Compare(left, right) == 0,
                ExpressionType.NotEqual => Compare(left, right) != 0,
                ExpressionType.GreaterThan => Compare(left, right) > 0,
                ExpressionType.GreaterThanOrEqual => Compare(left, right) >= 0,
                ExpressionType.LessThan => Compare(left, right) < 0,
                ExpressionType.LessThanOrEqual => Compare(left, right) <= 0,
                _ => throw new NotSupportedException($"Unsupported binary operator: {node.NodeType}")
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private object? VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains" && node.Method.DeclaringType == typeof(string))
        {
            var targetObject = Visit(node.Object);
            var argument = Visit(node.Arguments[0]);

            string? targetString = null;
            if (targetObject is JsonElement element) targetString = element.GetString();
            else targetString = targetObject as string;

            var substring = argument as string;

            if (targetString != null && substring != null)
                return targetString.Contains(substring, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        if (node.Method.Name == "StartsWith" && node.Method.DeclaringType == typeof(string))
        {
            var targetObject = Visit(node.Object);
            var argument = Visit(node.Arguments[0]);

            var substring = argument as string;
            if (substring == null) return false;

            if (targetObject is JsonElement element && element.ValueKind == JsonValueKind.String)
                return element.ValueEquals(substring);

            string? targetString = (targetObject is JsonElement element2) ? element2.GetString() : targetObject as string;
            return targetString?.StartsWith(substring, StringComparison.OrdinalIgnoreCase) ?? false;
        }

        if (node.Method.Name == "EndsWith" && node.Method.DeclaringType == typeof(string))
        {
            var targetObject = Visit(node.Object);
            var argument = Visit(node.Arguments[0]);

            var substring = argument as string;
            string? targetString = (targetObject is JsonElement element) ? element.GetString() : targetObject as string;

            return targetString?.EndsWith(substring!, StringComparison.OrdinalIgnoreCase) ?? false;
        }

        if (node.Method.Name == "Any" && node.Method.DeclaringType == typeof(Enumerable))
        {
            var sourceObject = Visit(node.Arguments[0]);

            if (sourceObject is System.Collections.IEnumerable enumerable)
            {
                if (node.Arguments.Count == 1)
                {
                    var enumerator = enumerable.GetEnumerator();
                    return enumerator.MoveNext();
                }

                if (node.Arguments.Count == 2 && node.Arguments[1] is LambdaExpression predicateLambda)
                {
                    foreach (var item in enumerable)
                    {
                        var param = predicateLambda.Parameters[0];
                        var replacer = new ParameterToConstantReplacer(param, item);
                        var bodyWithConstant = replacer.Visit(predicateLambda.Body);

                        var result = Visit(bodyWithConstant);
                        if (ConvertToBool(result))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        // Support List.Contains / Enumerable.Contains
        if (node.Method.Name == "Contains")
        {
            // Case 1: Instance method List<T>.Contains(item)
            if (node.Object != null && typeof(System.Collections.IEnumerable).IsAssignableFrom(node.Object.Type))
            {
                var collectionObj = Visit(node.Object);
                var argument = Visit(node.Arguments[0]);

                if (collectionObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (Compare(item, argument) == 0) return true;
                    }
                    return false;
                }
                return false;
            }

            // Case 2: Extension method Enumerable.Contains(source, item)
            if (node.Method.IsStatic && node.Arguments.Count >= 2 &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(node.Arguments[0].Type))
            {
                var collectionObj = Visit(node.Arguments[0]);
                var searchItem = Visit(node.Arguments[1]);

                if (collectionObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (Compare(item, searchItem) == 0) return true;
                    }
                    return false;
                }
                return false;
            }
        }

        throw new NotSupportedException($"Unsupported method call: {node.Method.Name}");
    }

    private object? VisitMember(MemberExpression node)
    {
        // 1. Direct access on Parameter: x.Prop
        if (node.Expression != null && node.Expression.NodeType == ExpressionType.Parameter)
        {
            var memberName = GetJsonName(node.Member);
            if (_rootElement.TryGetProperty(memberName, out var prop))
            {
                return prop;
            }
            return null;
        }

        // 2. Evaluate expression to get container
        object? container = null;
        if (node.Expression != null)
        {
            container = Visit(node.Expression);
        }

        // 3. If container is JsonElement, traverse nested JSON
        if (container is JsonElement jsonElement)
        {
            var memberName = GetJsonName(node.Member);
            if (jsonElement.ValueKind == JsonValueKind.Object &&
                jsonElement.TryGetProperty(memberName, out var prop))
            {
                return prop;
            }
            return null;
        }

        // 4. Regular CLR access
        if (node.Member is FieldInfo fi) return fi.GetValue(container);
        if (node.Member is PropertyInfo pi) return pi.GetValue(container);

        throw new NotSupportedException($"Unsupported member access: {node.Member.Name}");
    }

    private static string GetJsonName(MemberInfo member)
    {
        var attr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
        return attr?.Name ?? member.Name;
    }

    private int Compare(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        if (left is JsonElement leftElement) return CompareJsonWithObject(leftElement, right);
        if (right is JsonElement rightElement) return -CompareJsonWithObject(rightElement, left);

        if (left is IComparable && right is IComparable)
        {
            if (IsNumber(left) && IsNumber(right))
            {
                var leftDouble = Convert.ToDouble(left);
                var rightDouble = Convert.ToDouble(right);
                return leftDouble.CompareTo(rightDouble);
            }

            if (left is string leftString && right is string rightString)
            {
                return string.Compare(leftString, rightString, StringComparison.OrdinalIgnoreCase);
            }

            return Comparer<object>.Default.Compare(left, right);
        }

        throw new ArgumentException("Objects are not comparable");
    }

    private static int CompareJsonWithObject(JsonElement jsonElement, object? targetObject)
    {
        if (targetObject == null) return 1;

        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.String:
                var targetString = targetObject.ToString();
                if (jsonElement.ValueEquals(targetString)) return 0;
                return string.Compare(jsonElement.GetString(), targetString, StringComparison.OrdinalIgnoreCase);

            case JsonValueKind.Number:
                var targetDouble = Convert.ToDouble(targetObject);
                if (jsonElement.TryGetDouble(out var jsonDouble)) return jsonDouble.CompareTo(targetDouble);
                return 0;

            case JsonValueKind.True:
            case JsonValueKind.False:
                var jsonBoolean = jsonElement.GetBoolean();
                var targetBoolean = Convert.ToBoolean(targetObject);
                return jsonBoolean.CompareTo(targetBoolean);

            case JsonValueKind.Null:
                return -1;

            default:
                return string.Compare(jsonElement.ToString(), targetObject.ToString(), StringComparison.Ordinal);
        }
    }

    private static bool IsNumber(object val)
    {
        return val is int or long or double or float or decimal or byte or short;
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj is JsonElement elem)
            return elem.ValueKind == JsonValueKind.True;
        return obj is true;
    }
}
