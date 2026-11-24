using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Sql;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

/// <summary>Summarises SHOWPLAN_XML output: total cost, most expensive operators, warnings and missing indexes.</summary>
public sealed class ShowplanParser : IExecutionPlanParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public ExecutionPlanSummary Parse(string planXml)
    {
        XDocument doc;
        using (var reader = XmlReader.Create(new StringReader(planXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            doc = XDocument.Load(reader);

        var statement = doc.Descendants(Ns + "StmtSimple").FirstOrDefault();
        var totalCost = Double(statement?.Attribute("StatementSubTreeCost")?.Value);
        var totalRows = Double(statement?.Attribute("StatementEstRows")?.Value);

        var operators = doc.Descendants(Ns + "RelOp")
            .Select(r => new PlanOperator(
                r.Attribute("PhysicalOp")?.Value ?? "?",
                r.Attribute("LogicalOp")?.Value ?? "?",
                Double(r.Attribute("EstimateRows")?.Value),
                Double(r.Attribute("EstimatedTotalSubtreeCost")?.Value),
                r.Elements().SelectMany(e => e.Elements(Ns + "Object")).Select(o => $"{o.Attribute("Schema")?.Value}.{o.Attribute("Table")?.Value}".Trim('.')).FirstOrDefault()))
            .OrderByDescending(o => o.EstimatedSubtreeCost)
            .Take(10).ToList();

        var warnings = doc.Descendants(Ns + "Warnings").SelectMany(w => w.Elements()).Select(w => w.Name.LocalName).Distinct().ToList();

        var missing = doc.Descendants(Ns + "MissingIndex").Select(m =>
        {
            var table = $"{m.Attribute("Schema")?.Value}.{m.Attribute("Table")?.Value}";
            var cols = m.Elements(Ns + "ColumnGroup").Select(g => $"{g.Attribute("Usage")?.Value}: {string.Join(", ", g.Elements(Ns + "Column").Select(c => c.Attribute("Name")?.Value))}");
            return $"{table} ({string.Join("; ", cols)})";
        }).Distinct().ToList();

        return new ExecutionPlanSummary(totalCost, totalRows, operators, warnings, missing);
    }

    private static double Double(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? Math.Round(d, 4) : 0;
}
