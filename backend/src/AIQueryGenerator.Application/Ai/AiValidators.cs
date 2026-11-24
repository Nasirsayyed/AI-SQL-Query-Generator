using FluentValidation;

namespace AIQueryGenerator.Application.Ai;

public sealed class GenerateSqlRequestValidator : AbstractValidator<GenerateSqlRequest>
{
    public GenerateSqlRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Question).NotEmpty().MinimumLength(3).MaximumLength(2000);
        RuleFor(x => x.SchemaName).MaximumLength(128);
    }
}

public sealed class ExplainSqlRequestValidator : AbstractValidator<ExplainSqlRequest>
{
    public ExplainSqlRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Sql).NotEmpty().MaximumLength(20_000);
    }
}

public sealed class OptimizeSqlRequestValidator : AbstractValidator<OptimizeSqlRequest>
{
    public OptimizeSqlRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Sql).NotEmpty().MaximumLength(20_000);
    }
}

public sealed class SuggestChartRequestValidator : AbstractValidator<SuggestChartRequest>
{
    public SuggestChartRequestValidator()
    {
        RuleFor(x => x.Columns).NotEmpty().Must(c => c.Count <= 200);
        RuleFor(x => x.Question).MaximumLength(2000);
        RuleFor(x => x.RowCount).GreaterThanOrEqualTo(0);
    }
}
