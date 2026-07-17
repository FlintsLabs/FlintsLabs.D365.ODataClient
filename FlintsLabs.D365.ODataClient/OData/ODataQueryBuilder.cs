namespace FlintsLabs.D365.ODataClient.OData;

internal sealed class ODataQueryBuilder
{
    private readonly Dictionary<string, string> _single =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _expands = [];

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _single[name] = value;
    }

    public bool TryGet(string name, out string value)
    {
        return _single.TryGetValue(name, out value!);
    }

    public void Remove(string name)
    {
        _single.Remove(name);
    }

    public void AddExpand(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _expands.Add(value);
    }

    public string Build(
        string entity,
        bool crossCompany,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        var options = new Dictionary<string, string>(_single, StringComparer.OrdinalIgnoreCase);
        if (_expands.Count > 0)
            options["$expand"] = string.Join(',', _expands);
        if (overrides is not null)
        {
            foreach (var option in overrides)
                options[option.Key] = option.Value;
        }

        var queryParts = new List<string>();
        if (crossCompany)
            queryParts.Add("cross-company=true");
        queryParts.AddRange(options.Select(option =>
            $"{EncodeName(option.Key)}={Uri.EscapeDataString(option.Value)}"));

        return queryParts.Count == 0
            ? entity
            : $"{entity}?{string.Join('&', queryParts)}";
    }

    private static string EncodeName(string name)
    {
        return Uri.EscapeDataString(name)
            .Replace("%24", "$", StringComparison.OrdinalIgnoreCase);
    }
}
