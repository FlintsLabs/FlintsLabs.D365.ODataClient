namespace FlintsLabs.D365.ODataClient.Helpers;

/// <summary>
/// URL building extension methods for D365 OData queries
/// </summary>
public static class D365Helper
{
    /// <summary>
    /// Initialize URL with query string start
    /// </summary>
    public static string InitUrl(string? url = "")
    {
        var urlText = string.IsNullOrWhiteSpace(url) ? "?" : url + "?";
        return urlText;
    }

    /// <summary>
    /// Add cross-company parameter
    /// </summary>
    public static string CrossCompany(this string? url, bool isCrossCompany = true)
    {
        var urlText = url.CheckUrl() + "cross-company=" + (isCrossCompany ? "true" : "false");
        return urlText;
    }

    /// <summary>
    /// Add cross-company parameter (static version)
    /// </summary>
    public static string CrossCompany(bool isCrossCompany = true)
    {
        var urlText = CheckUrl("") + "cross-company=" + (isCrossCompany ? "true" : "false");
        return urlText;
    }

    /// <summary>
    /// Add $skip parameter
    /// </summary>
    public static string Skip(this string url, int numberRecord)
    {
        var urlText = url.CheckUrl() + $"$skip={numberRecord}";
        return urlText;
    }

    /// <summary>
    /// Add $top parameter
    /// </summary>
    public static string Top(this string url, int numberRecord)
    {
        var urlText = url.CheckUrl() + $"$top={numberRecord}";
        return urlText;
    }

    /// <summary>
    /// Add $orderby ascending
    /// </summary>
    public static string OrderBy(this string url, string property)
    {
        var urlText = url.CheckUrl() + $"$orderby={property} asc";
        return urlText;
    }

    /// <summary>
    /// Add $orderby descending
    /// </summary>
    public static string OrderByDescending(this string url, string property)
    {
        var urlText = url.CheckUrl() + $"$orderby={property} desc";
        return urlText;
    }

    /// <summary>
    /// Add $filter with initial condition
    /// </summary>
    public static string Filter(this string url, string property, string operand, object value)
    {
        return value switch
        {
            string val => url.CheckUrl() + $"$filter={property} {operand} '{val}'",
            double valInt => url.CheckUrl() + $"$filter={property} {operand} {valInt}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Add $filter for dataAreaId
    /// </summary>
    public static string FilterDataArea(this string url, string value)
    {
        var urlText = url.CheckUrl() + $"$filter=dataAreaId eq '{value}'";
        return urlText;
    }

    /// <summary>
    /// Add AND condition to filter
    /// </summary>
    public static string And(this string url, string property, string operand, object value, bool addSingleQuote = true,
        string dataEntity = "")
    {
        return value switch
        {
            string val when addSingleQuote => url + $" and {property} {operand} {dataEntity}'{val}'",
            string val when !addSingleQuote => url + $" and {property} {operand} {dataEntity}{val}",
            double valInt => url + $" and {property} {operand} {valInt}",
            DateTime valDate => url + $" and {property} {operand} {valDate}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Add OR condition to filter
    /// </summary>
    public static string Or(this string url, string property, string operand, object value, bool addSingleQuote = true)
    {
        return value switch
        {
            string val when addSingleQuote => url + $" or {property} {operand} '{val}'",
            string val when !addSingleQuote => url + $" or {property} {operand} {val}",
            double valInt => url + $" or {property} {operand} {valInt}",
            DateTime valDate => url + $" or {property} {operand} {valDate}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Add OR conditions from list
    /// </summary>
    public static string Or(this string url, string property, string operand, List<object> values,
        bool addSingleQuote = true)
    {
        var strCriteria = string.Empty;

        foreach (var value in values)
        {
            strCriteria += value switch
            {
                string val when addSingleQuote => url + $" or {property} {operand} '{val}'",
                string val when !addSingleQuote => url + $" or {property} {operand} {val}",
                double valInt => url + $" or {property} {operand} {valInt}",
                DateTime valDate => url + $" or {property} {operand} {valDate}",
                _ => string.Empty
            };
        }

        return strCriteria;
    }

    /// <summary>
    /// Add IN condition (multiple OR)
    /// </summary>
    public static string In(this string url, string property, string operand, List<object> values,
        bool addSingleQuote = true)
    {
        if (values is null) throw new ArgumentNullException(nameof(values));

        var firstVal = values.First();
        values.Remove(firstVal);

        var strCriteria = url.And(property, operand, firstVal);

        if (!values.Any()) return strCriteria;

        foreach (var value in values)
        {
            strCriteria += value switch
            {
                string val when addSingleQuote => $" or {property} {operand} '{val}'",
                string val when !addSingleQuote => $" or {property} {operand} {val}",
                double valInt => $" or {property} {operand} {valInt}",
                DateTime valDate => $" or {property} {operand} {valDate}",
                _ => string.Empty
            };
        }

        return strCriteria;
    }

    /// <summary>
    /// Add $select parameter
    /// </summary>
    public static string Select(this string url, string property)
    {
        return url.CheckUrl() + $"$select={property}";
    }

    /// <summary>
    /// Add $select parameter with multiple properties
    /// </summary>
    public static string Select(this string url, IEnumerable<string> properties)
    {
        return Select(url, string.Join(',', properties));
    }

    /// <summary>
    /// Add entity key to URL
    /// </summary>
    public static string RemoveProperties(this string url, Dictionary<string, string> keyValues)
    {
        var liststr = keyValues.Select(a => $"{a.Key}='{a.Value}'").ToList();
        var str = string.Join(',', liststr);

        return url.Replace("?", $"({str})?");
    }

    /// <summary>
    /// Add single key to URL
    /// </summary>
    public static string RemoveProperties(this string url, string property)
    {
        return url.Replace("?", $"({property})?");
    }

    /// <summary>
    /// Ensure URL has proper query string separator
    /// </summary>
    private static string CheckUrl(this string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            url = "";

        url += url.Contains('?') ? "&" : "?";
        return url;
    }
}
