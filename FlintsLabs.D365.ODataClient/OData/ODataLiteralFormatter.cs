using System.Globalization;
using FlintsLabs.D365.ODataClient.Enums;

namespace FlintsLabs.D365.ODataClient.OData;

internal static class ODataLiteralFormatter
{
    public static string Format(
        object? value,
        D365BooleanFormatting booleanFormatting = D365BooleanFormatting.NoYesEnum)
    {
        return value switch
        {
            null => "null",
            string text => IsD365EnumLiteral(text) ? text : Quote(text),
            char character => Quote(character.ToString()),
            bool boolean => FormatBoolean(boolean, booleanFormatting),
            Guid guid => guid.ToString(),
            DateTime dateTime => FormatDateTime(dateTime),
            DateTimeOffset dateTimeOffset => FormatDateTime(dateTimeOffset.UtcDateTime),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Enum enumValue => Quote(enumValue.ToString()),
            sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            _ => throw new NotSupportedException(
                $"OData literal type '{value.GetType().FullName}' is not supported.")
        };
    }

    private static string FormatBoolean(
        bool value,
        D365BooleanFormatting booleanFormatting)
    {
        return booleanFormatting == D365BooleanFormatting.Literal
            ? value ? "true" : "false"
            : value ? D365NoYes.Yes : D365NoYes.No;
    }

    private static string FormatDateTime(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string Quote(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static bool IsD365EnumLiteral(string value)
    {
        return value.StartsWith("Microsoft.Dynamics.DataEntities.", StringComparison.Ordinal)
            || value.StartsWith("Microsoft.Dynamics.AX.Application.", StringComparison.Ordinal);
    }
}
