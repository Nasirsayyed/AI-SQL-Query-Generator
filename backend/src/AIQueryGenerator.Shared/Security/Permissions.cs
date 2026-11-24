namespace AIQueryGenerator.Shared.Security;

/// <summary>
/// Fine-grained permission codes. Authorization is always evaluated on the server using these codes;
/// the React client only uses them to hide controls.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "dashboard.view";
    public const string QueryGenerate = "query.generate";
    public const string QueryEditSql = "query.edit_sql";
    public const string QueryExecute = "query.execute";
    public const string QueryExplain = "query.explain";
    public const string ResultsExport = "results.export";
    public const string ChartsView = "charts.view";
    public const string HistoryView = "history.view";
    public const string HistoryViewAll = "history.view_all";
    public const string HistoryDelete = "history.delete";
    public const string SavedQueriesManage = "saved_queries.manage";
    public const string SavedQueriesShare = "saved_queries.share";
    public const string SharedReportsView = "shared_reports.view";
    public const string SchemaView = "schema.view";
    public const string SchemaConfigure = "schema.configure";
    public const string ConnectionsView = "connections.view";
    public const string ConnectionsManage = "connections.manage";
    public const string UsersManage = "users.manage";
    public const string AdminsManage = "admins.manage";
    public const string AiSettingsManage = "ai_settings.manage";
    public const string AuditView = "audit.view";

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [DashboardView] = "View the analytics dashboard",
        [QueryGenerate] = "Generate SQL from natural language",
        [QueryEditSql] = "Edit SQL manually in the editor",
        [QueryExecute] = "Execute validated read-only queries",
        [QueryExplain] = "Request AI explanations of SQL",
        [ResultsExport] = "Export results to CSV/Excel",
        [ChartsView] = "View charts of query results",
        [HistoryView] = "View own query history",
        [HistoryViewAll] = "View the query history of all users",
        [HistoryDelete] = "Delete query history records",
        [SavedQueriesManage] = "Create and manage saved queries",
        [SavedQueriesShare] = "Share saved queries with other users",
        [SharedReportsView] = "View shared saved queries and their results",
        [SchemaView] = "Browse permitted schema metadata",
        [SchemaConfigure] = "Configure which schema objects AI may use",
        [ConnectionsView] = "View database connections available to the user",
        [ConnectionsManage] = "Create, edit and test database connections",
        [UsersManage] = "Manage users and role assignments",
        [AdminsManage] = "Manage administrator accounts",
        [AiSettingsManage] = "Manage AI provider settings",
        [AuditView] = "View audit logs",
    };

    /// <summary>Default role → permission matrix seeded into the application database.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> DefaultRolePermissions = new Dictionary<string, string[]>
    {
        [Roles.SuperAdmin] = Descriptions.Keys.ToArray(),
        [Roles.Admin] = Descriptions.Keys.Where(p => p != AdminsManage).ToArray(),
        [Roles.Developer] =
        [
            DashboardView, QueryGenerate, QueryEditSql, QueryExecute, QueryExplain, ResultsExport, ChartsView,
            HistoryView, HistoryDelete, SavedQueriesManage, SavedQueriesShare, SharedReportsView, SchemaView, ConnectionsView,
        ],
        [Roles.Analyst] =
        [
            DashboardView, QueryGenerate, QueryExecute, QueryExplain, ResultsExport, ChartsView,
            HistoryView, HistoryDelete, SavedQueriesManage, SharedReportsView, SchemaView, ConnectionsView,
        ],
        [Roles.Viewer] = [DashboardView, SharedReportsView, ChartsView, ConnectionsView],
    };
}
