using AIQueryGenerator.Application.Ai;
using AIQueryGenerator.Application.Audit;
using AIQueryGenerator.Application.Auth;
using AIQueryGenerator.Application.Connections;
using AIQueryGenerator.Application.Dashboard;
using AIQueryGenerator.Application.History;
using AIQueryGenerator.Application.SavedQueries;
using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Application.Settings;
using AIQueryGenerator.Application.Sql;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AIQueryGenerator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IConnectionAccessService, ConnectionAccessService>();
        services.AddScoped<IConnectionService, ConnectionService>();
        services.AddScoped<ISchemaSyncService, SchemaSyncService>();
        services.AddScoped<ISchemaCatalogService, SchemaCatalogService>();
        services.AddScoped<ISchemaService, SchemaService>();
        services.AddScoped<IQueryExecutionPipeline, QueryExecutionPipeline>();
        services.AddScoped<ISqlQueryService, SqlQueryService>();
        services.AddScoped<IAiSqlService, AiSqlService>();
        services.AddScoped<IQueryHistoryService, QueryHistoryService>();
        services.AddScoped<ISavedQueryService, SavedQueryService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAiSettingsService, AiSettingsService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
