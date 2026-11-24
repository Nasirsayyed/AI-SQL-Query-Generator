namespace AIQueryGenerator.Application.Ai;

/// <summary>
/// JSON Schemas enforced through the provider's structured-output mode (OpenAI "strict" json_schema), so responses
/// are guaranteed to be a single JSON object of this shape rather than free text. Parsed results are validated again.
/// </summary>
public static class AiJsonSchemas
{
    public static readonly string[] ChartTypes = ["bar", "horizontalBar", "line", "area", "pie", "doughnut", "scatter"];
    public static readonly string[] Aggregations = ["none", "sum", "avg", "count", "min", "max"];

    private const string ChartSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["recommended", "chartType", "xAxis", "yAxis", "aggregation", "title", "reason"],
          "properties": {
            "recommended": { "type": "boolean" },
            "chartType": { "type": ["string", "null"], "enum": ["bar", "horizontalBar", "line", "area", "pie", "doughnut", "scatter", null] },
            "xAxis": { "type": ["string", "null"] },
            "yAxis": { "type": ["string", "null"] },
            "aggregation": { "type": ["string", "null"], "enum": ["none", "sum", "avg", "count", "min", "max", null] },
            "title": { "type": ["string", "null"] },
            "reason": { "type": ["string", "null"] }
          }
        }
        """;

    public static readonly string SqlGeneration = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["success", "needsClarification", "clarificationQuestion", "sqlQuery", "queryTitle", "explanation",
                       "tablesUsed", "columnsUsed", "parameters", "warnings", "chartSuggestion"],
          "properties": {
            "success": { "type": "boolean" },
            "needsClarification": { "type": "boolean" },
            "clarificationQuestion": { "type": ["string", "null"] },
            "sqlQuery": { "type": ["string", "null"] },
            "queryTitle": { "type": "string" },
            "explanation": { "type": "string" },
            "tablesUsed": { "type": "array", "items": { "type": "string" } },
            "columnsUsed": { "type": "array", "items": { "type": "string" } },
            "parameters": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["name", "type", "value", "description"],
                "properties": {
                  "name": { "type": "string" },
                  "type": { "type": "string", "enum": ["String", "Int", "BigInt", "Decimal", "Bool", "Date", "DateTime", "Guid"] },
                  "value": { "type": ["string", "null"] },
                  "description": { "type": ["string", "null"] }
                }
              }
            },
            "warnings": { "type": "array", "items": { "type": "string" } },
            "chartSuggestion": CHART
          }
        }
        """.Replace("CHART", ChartSchema);

    public static readonly string Chart = ChartSchema;

    public static readonly string Explanation = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "steps", "beginnerExplanation", "notes"],
          "properties": {
            "summary": { "type": "string" },
            "steps": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["clause", "description"],
                "properties": { "clause": { "type": "string" }, "description": { "type": "string" } }
              }
            },
            "beginnerExplanation": { "type": ["string", "null"] },
            "notes": { "type": "array", "items": { "type": "string" } }
          }
        }
        """;

    public static readonly string Optimization = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["summary", "suggestions", "optimizedSql"],
          "properties": {
            "summary": { "type": "string" },
            "suggestions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["title", "description", "severity"],
                "properties": {
                  "title": { "type": "string" },
                  "description": { "type": "string" },
                  "severity": { "type": "string", "enum": ["info", "warning", "critical"] }
                }
              }
            },
            "optimizedSql": { "type": ["string", "null"] }
          }
        }
        """;
}
