using FluentValidation;

namespace AIQueryGenerator.Application.Sql;

public sealed class ValidateSqlRequestValidator : AbstractValidator<ValidateSqlRequest>
{
    public ValidateSqlRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Sql).NotEmpty().MaximumLength(50_000);
        RuleFor(x => x.Parameters).Must(p => p is null || p.Count <= 50);
    }
}

public sealed class ExecuteSqlRequestValidator : AbstractValidator<ExecuteSqlRequest>
{
    public ExecuteSqlRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Sql).NotEmpty().MaximumLength(50_000);
        RuleFor(x => x.Parameters).NotNull().Must(p => p.Count <= 50);
        RuleFor(x => x.QueryTitle).MaximumLength(200);
    }
}

public sealed class FormatSqlRequestValidator : AbstractValidator<FormatSqlRequest>
{
    public FormatSqlRequestValidator() => RuleFor(x => x.Sql).NotEmpty().MaximumLength(50_000);
}

public sealed class ExplainPlanRequestValidator : AbstractValidator<ExplainPlanRequest>
{
    public ExplainPlanRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.Sql).NotEmpty().MaximumLength(50_000);
    }
}
