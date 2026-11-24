using AIQueryGenerator.Application.Sql;

namespace AIQueryGenerator.Application.Ai;

/// <summary>Validates AI chart configurations against the actual result columns and provides a deterministic fallback.</summary>
public static class ChartSuggestionPolicy
{
    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "int", "bigint", "smallint", "tinyint", "decimal", "numeric", "money", "smallmoney", "float", "real",
        "Int32", "Int64", "Int16", "Byte", "Decimal", "Double", "Single",
    };

    private static readonly HashSet<string> DateTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "datetime", "datetime2", "smalldatetime", "datetimeoffset", "DateTime", "DateTimeOffset", "DateOnly",
    };

    public static bool IsNumeric(ResultColumn c) => NumericTypes.Contains(c.DataType) || NumericTypes.Contains(c.ClrType);
    public static bool IsDate(ResultColumn c) => DateTypes.Contains(c.DataType) || DateTypes.Contains(c.ClrType);

    /// <summary>Returns the suggestion only when it references existing columns with suitable types; otherwise null.</summary>
    internal static ChartSuggestionDto? Sanitize(AiChart? chart, IReadOnlyList<ResultColumn>? columns, string source)
    {
        if (chart is null) return null;
        if (!chart.Recommended) return new ChartSuggestionDto(false, null, null, null, null, null, Clean(chart.Reason), source);

        var type = AiJsonSchemas.ChartTypes.FirstOrDefault(t => string.Equals(t, chart.ChartType, StringComparison.OrdinalIgnoreCase));
        var aggregation = AiJsonSchemas.Aggregations.FirstOrDefault(a => string.Equals(a, chart.Aggregation, StringComparison.OrdinalIgnoreCase)) ?? "none";
        if (type is null || string.IsNullOrWhiteSpace(chart.XAxis) || string.IsNullOrWhiteSpace(chart.YAxis)) return null;

        string? x = chart.XAxis.Trim(), y = chart.YAxis.Trim();
        if (columns is not null)
        {
            var xCol = columns.FirstOrDefault(c => string.Equals(c.Name, x, StringComparison.OrdinalIgnoreCase));
            var yCol = columns.FirstOrDefault(c => string.Equals(c.Name, y, StringComparison.OrdinalIgnoreCase));
            if (xCol is null || yCol is null) return null;
            if (!IsNumeric(yCol) && aggregation != "count") return null;
            x = xCol.Name;
            y = yCol.Name;
        }

        return new ChartSuggestionDto(true, type, x, y, aggregation, Clean(chart.Title), Clean(chart.Reason), source);
    }

    /// <summary>Deterministic recommendation from column types, used when the AI provider is unavailable.</summary>
    public static ChartSuggestionDto Heuristic(string? question, IReadOnlyList<ResultColumn> columns, int rowCount)
    {
        var numeric = columns.Where(IsNumeric).ToList();
        var date = columns.FirstOrDefault(IsDate);
        var category = columns.FirstOrDefault(c => !IsNumeric(c) && !IsDate(c));
        var q = question?.ToLowerInvariant() ?? string.Empty;

        if (numeric.Count == 0 || rowCount < 2)
            return new ChartSuggestionDto(false, null, null, null, null, null, "The result has no numeric measure to chart.", "heuristic");

        var timeLike = date is not null || q.Contains("month") || q.Contains("daily") || q.Contains("trend") || q.Contains("year") || q.Contains("week");
        var x = date ?? category ?? columns.First(c => c != numeric[0]);
        var y = numeric.FirstOrDefault(n => n != x) ?? numeric[0];
        if (x == y) return new ChartSuggestionDto(false, null, null, null, null, null, "The result needs a category and a numeric column.", "heuristic");

        string type;
        if (timeLike && x == date || (timeLike && category is not null && (category.Name.Contains("month", StringComparison.OrdinalIgnoreCase) || category.Name.Contains("date", StringComparison.OrdinalIgnoreCase))))
            type = "line";
        else if ((q.Contains("share") || q.Contains("distribution") || q.Contains("percentage") || q.Contains("breakdown")) && rowCount <= 8)
            type = "pie";
        else if (rowCount > 12 && category is not null)
            type = "horizontalBar";
        else if (category is null && numeric.Count >= 2)
            type = "scatter";
        else
            type = "bar";

        return new ChartSuggestionDto(true, type, x.Name, y.Name, "none", null, "Chosen from the result column types.", "heuristic");
    }

    private static string? Clean(string? s) => Common.Text.SanitizeForPrompt(s, 200);
}
