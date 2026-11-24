using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.UnitTests.Ai;

public class AiResponseParsingTests
{
    private sealed class Shape
    {
        public bool Success { get; set; }
        public string? SqlQuery { get; set; }
    }

    [Fact]
    public void Parses_valid_json_object() =>
        Assert.Equal("SELECT 1", AiResponseParser.Parse<Shape>("""{"success":true,"sqlQuery":"SELECT 1"}""").SqlQuery);

    [Theory]
    [InlineData("")]
    [InlineData("Here you go: {\"success\":true}")]
    [InlineData("```json\n{\"success\":true}\n```")]
    [InlineData("{\"success\":true,}")]
    [InlineData("[1,2]")]
    [InlineData("{\"success\": tru}")]
    public void Rejects_non_json_or_wrapped_output(string content)
    {
        var ex = Assert.Throws<ExternalServiceException>(() => AiResponseParser.Parse<Shape>(content));
        Assert.Equal(ErrorCodes.AiInvalidResponse, ex.ErrorCode);
    }

    [Fact]
    public void Json_schemas_are_valid_json_and_strict()
    {
        foreach (var schema in new[] { AiJsonSchemas.SqlGeneration, AiJsonSchemas.Chart, AiJsonSchemas.Explanation, AiJsonSchemas.Optimization })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(schema);
            Assert.False(doc.RootElement.GetProperty("additionalProperties").GetBoolean());
            var required = doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
            var props = doc.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.Equal(props, required); // OpenAI strict mode requires every property to be listed
        }
    }

    [Fact]
    public void Chart_suggestion_rejects_unknown_columns_and_types()
    {
        var columns = new[] { new ResultColumn("Month", "date", "DateTime"), new ResultColumn("Revenue", "decimal", "Decimal") };
        var ok = ChartSuggestionPolicy.Heuristic("monthly revenue", columns, 12);
        Assert.Equal("line", ok.ChartType);
        Assert.Equal("Month", ok.XAxis);
        Assert.Equal("Revenue", ok.YAxis);

        var none = ChartSuggestionPolicy.Heuristic("names", [new ResultColumn("Name", "nvarchar", "String")], 5);
        Assert.False(none.Recommended);
    }

    [Theory]
    [InlineData("share of revenue by region", 5, "pie")]
    [InlineData("revenue by region", 5, "bar")]
    [InlineData("revenue by product", 40, "horizontalBar")]
    public void Heuristic_picks_chart_type(string question, int rows, string expected)
    {
        var columns = new[] { new ResultColumn("Region", "nvarchar", "String"), new ResultColumn("Revenue", "decimal", "Decimal") };
        Assert.Equal(expected, ChartSuggestionPolicy.Heuristic(question, columns, rows).ChartType);
    }
}
