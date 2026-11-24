using System.Globalization;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

/// <summary>
/// Converts SQL values into JSON-friendly values. Numbers that JavaScript cannot represent exactly
/// (64-bit integers beyond 2^53, decimals with more than 15 significant digits) are returned as strings.
/// </summary>
public static class ValueConverter
{
    private const long MaxSafeInteger = 9_007_199_254_740_991;

    public static object? Convert(object value, string sqlType, int maxCellLength) => value switch
    {
        DBNull => null,
        string s => s.Length > maxCellLength ? s[..maxCellLength] + "…" : s,
        long l => l is > MaxSafeInteger or < -MaxSafeInteger ? l.ToString(CultureInfo.InvariantCulture) : l,
        decimal d => SignificantDigits(d) > 15 ? d.ToString(CultureInfo.InvariantCulture) : d,
        DateTime dt when sqlType.Equals("date", StringComparison.OrdinalIgnoreCase) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => "0x" + System.Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64))) + (bytes.Length > 64 ? "…" : string.Empty),
        Guid g => g.ToString(),
        float f => (double)f,
        bool or int or short or byte or double => value,
        _ => value.ToString() is { } text && text.Length > maxCellLength ? text[..maxCellLength] + "…" : value.ToString(),
    };

    private static int SignificantDigits(decimal d)
    {
        var digits = decimal.Abs(d).ToString(CultureInfo.InvariantCulture).Replace(".", string.Empty).TrimStart('0');
        return digits.Length;
    }
}
