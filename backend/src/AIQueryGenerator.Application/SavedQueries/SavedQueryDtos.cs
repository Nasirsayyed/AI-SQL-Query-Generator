using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Shared.Contracts;
using FluentValidation;

namespace AIQueryGenerator.Application.SavedQueries;

public sealed class SavedQueryQuery : PagedRequest
{
    public string? Category { get; set; }
    public bool FavoritesOnly { get; set; }

    /// <summary>"mine", "shared" or "all" (default: everything visible to the caller).</summary>
    public string? Scope { get; set; }
    public int? ConnectionId { get; set; }
}

public sealed record SavedQueryDto(
    int SavedQueryId,
    string QueryTitle,
    string? Description,
    string? UserQuestion,
    string SqlQuery,
    string? Category,
    bool IsShared,
    bool IsFavorite,
    bool IsOwner,
    string OwnerName,
    int ConnectionId,
    string ConnectionName,
    IReadOnlyList<QueryParameterDto> Parameters,
    string? ChartConfigJson,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SaveQueryRequest
{
    public int ConnectionId { get; init; }
    public string QueryTitle { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? UserQuestion { get; init; }
    public string SqlQuery { get; init; } = string.Empty;
    public string? Category { get; init; }
    public bool IsShared { get; init; }
    public IReadOnlyList<QueryParameterDto> Parameters { get; init; } = [];
    public string? ChartConfigJson { get; init; }

    /// <summary>Required for roles without SQL editing: the AI generation the SQL came from.</summary>
    public long? SourceQueryHistoryId { get; init; }
}

public sealed record RunSavedQueryRequest(IReadOnlyList<QueryParameterDto>? Parameters = null);

public sealed record FavoriteResultDto(int SavedQueryId, bool IsFavorite);

public sealed class SaveQueryRequestValidator : AbstractValidator<SaveQueryRequest>
{
    public SaveQueryRequestValidator()
    {
        RuleFor(x => x.ConnectionId).GreaterThan(0);
        RuleFor(x => x.QueryTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.UserQuestion).MaximumLength(2000);
        RuleFor(x => x.SqlQuery).NotEmpty().MaximumLength(50_000);
        RuleFor(x => x.Category).MaximumLength(100);
        RuleFor(x => x.ChartConfigJson).MaximumLength(4000);
        RuleFor(x => x.Parameters).NotNull().Must(p => p.Count <= 25);
    }
}
