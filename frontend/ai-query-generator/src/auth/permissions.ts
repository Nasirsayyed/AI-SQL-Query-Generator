/** Permission codes mirrored from the backend (AIQueryGenerator.Shared.Security.Permissions). UI hints only. */
export const Permissions = {
  DashboardView: 'dashboard.view',
  QueryGenerate: 'query.generate',
  QueryEditSql: 'query.edit_sql',
  QueryExecute: 'query.execute',
  QueryExplain: 'query.explain',
  ResultsExport: 'results.export',
  ChartsView: 'charts.view',
  HistoryView: 'history.view',
  HistoryViewAll: 'history.view_all',
  HistoryDelete: 'history.delete',
  SavedQueriesManage: 'saved_queries.manage',
  SavedQueriesShare: 'saved_queries.share',
  SharedReportsView: 'shared_reports.view',
  SchemaView: 'schema.view',
  SchemaConfigure: 'schema.configure',
  ConnectionsView: 'connections.view',
  ConnectionsManage: 'connections.manage',
  UsersManage: 'users.manage',
  AdminsManage: 'admins.manage',
  AiSettingsManage: 'ai_settings.manage',
  AuditView: 'audit.view',
} as const;

export type PermissionCode = (typeof Permissions)[keyof typeof Permissions];

export const RoleNames = ['SuperAdmin', 'Admin', 'Developer', 'Analyst', 'Viewer'] as const;
