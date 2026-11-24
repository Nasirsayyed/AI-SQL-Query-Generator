import { api } from './client';
import type * as T from './types';

/** Typed wrappers for every backend endpoint, grouped by module. */
export const authApi = {
  login: (email: string, password: string) => api.post<T.AuthResponse>('/api/auth/login', { email, password }),
  logout: () => api.post<object>('/api/auth/logout'),
  profile: () => api.get<T.UserProfile>('/api/auth/profile'),
  changePassword: (currentPassword: string, newPassword: string) =>
    api.post<object>('/api/auth/change-password', { currentPassword, newPassword }),
};

export const connectionsApi = {
  accessible: () => api.get<T.ConnectionSummary[]>('/api/connections'),
  managed: () => api.get<T.Connection[]>('/api/connections/manage'),
  get: (id: number) => api.get<T.Connection>(`/api/connections/${id}`),
  create: (body: T.SaveConnectionRequest) => api.post<T.Connection>('/api/connections', body),
  update: (id: number, body: T.SaveConnectionRequest) => api.put<T.Connection>(`/api/connections/${id}`, body),
  remove: (id: number) => api.delete<object>(`/api/connections/${id}`),
  test: (id: number) => api.post<T.ConnectionTestResult>(`/api/connections/${id}/test`),
  setActive: (id: number, isActive: boolean) => api.post<T.Connection>(`/api/connections/${id}/active`, { isActive }),
  refreshSchema: (id: number, full = false) => api.post<T.SchemaRefreshResult>(`/api/connections/${id}/refresh-schema?full=${full}`),
  allowedObjects: (id: number) => api.get<T.AllowedObjectRule[]>(`/api/connections/${id}/allowed-objects`),
  updateAllowedObjects: (id: number, rules: T.AllowedObjectRule[]) =>
    api.put<T.AllowedObjectRule[]>(`/api/connections/${id}/allowed-objects`, { rules }),
};

export const schemaApi = {
  overview: (connectionId: number) => api.get<T.SchemaOverview>(`/api/schema/${connectionId}`),
  tables: (connectionId: number, schemaName?: string) => api.get<T.TableSummary[]>(`/api/schema/${connectionId}/tables`, { schemaName }),
  table: (connectionId: number, tableName: string) =>
    api.get<T.TableDetail>(`/api/schema/${connectionId}/tables/${encodeURIComponent(tableName)}`),
  search: (connectionId: number, q: string) => api.get<T.SchemaSearchResult[]>(`/api/schema/${connectionId}/search`, { q }),
  completions: (connectionId: number) => api.get<T.CompletionTable[]>(`/api/schema/${connectionId}/completions`),
};

export const aiApi = {
  status: () => api.get<T.AiStatus>('/api/ai/status'),
  generateSql: (connectionId: number, question: string, schemaName?: string | null, signal?: AbortSignal) =>
    api.post<T.GenerateSqlResponse>('/api/ai/generate-sql', { connectionId, question, schemaName: schemaName || null }, signal),
  explainSql: (connectionId: number, sql: string, beginnerFriendly = true) =>
    api.post<T.ExplainSqlResponse>('/api/ai/explain-sql', { connectionId, sql, beginnerFriendly }),
  suggestChart: (question: string | null, columns: T.ResultColumn[], rowCount: number) =>
    api.post<T.ChartSuggestion>('/api/ai/suggest-chart', { question, columns, rowCount }),
  optimizeSql: (connectionId: number, sql: string) => api.post<T.OptimizeSqlResponse>('/api/ai/optimize-sql', { connectionId, sql }),
};

export const sqlApi = {
  validate: (connectionId: number, sql: string, parameters: T.QueryParameter[]) =>
    api.post<T.SqlValidationResponse>('/api/sql/validate', { connectionId, sql, parameters }),
  execute: (
    body: { connectionId: number; sql: string; parameters: T.QueryParameter[]; validationToken?: string | null; queryHistoryId?: number | null; queryTitle?: string | null },
    signal?: AbortSignal,
  ) => api.post<T.QueryResult>('/api/sql/execute', body, signal),
  format: (sql: string) => api.post<{ sql: string }>('/api/sql/format', { sql }),
  explainPlan: (connectionId: number, sql: string, parameters: T.QueryParameter[]) =>
    api.post<{ summary: T.ExecutionPlanSummary; planXml?: string | null }>('/api/sql/explain-plan', { connectionId, sql, parameters }),
};

export interface HistoryFilter {
  page: number;
  pageSize: number;
  search?: string;
  from?: string;
  to?: string;
  userId?: number;
  connectionId?: number;
  status?: string;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
}

export const historyApi = {
  list: (filter: HistoryFilter) => api.get<T.PagedResult<T.QueryHistoryItem>>('/api/query-history', filter),
  get: (id: number) => api.get<T.QueryHistoryDetail>(`/api/query-history/${id}`),
  remove: (id: number) => api.delete<object>(`/api/query-history/${id}`),
  rerun: (id: number, parameters?: T.QueryParameter[]) => api.post<T.QueryResult>(`/api/query-history/${id}/rerun`, { parameters: parameters ?? null }),
};

export interface SavedFilter {
  page: number;
  pageSize: number;
  search?: string;
  category?: string;
  favoritesOnly?: boolean;
  scope?: 'mine' | 'shared' | 'all';
  connectionId?: number;
}

export const savedQueriesApi = {
  list: (filter: SavedFilter) => api.get<T.PagedResult<T.SavedQuery>>('/api/saved-queries', filter),
  categories: () => api.get<string[]>('/api/saved-queries/categories'),
  get: (id: number) => api.get<T.SavedQuery>(`/api/saved-queries/${id}`),
  create: (body: T.SaveQueryRequest) => api.post<T.SavedQuery>('/api/saved-queries', body),
  update: (id: number, body: T.SaveQueryRequest) => api.put<T.SavedQuery>(`/api/saved-queries/${id}`, body),
  remove: (id: number) => api.delete<object>(`/api/saved-queries/${id}`),
  toggleFavorite: (id: number) => api.post<{ savedQueryId: number; isFavorite: boolean }>(`/api/saved-queries/${id}/favorite`),
  run: (id: number, parameters?: T.QueryParameter[]) => api.post<T.QueryResult>(`/api/saved-queries/${id}/run`, { parameters: parameters ?? null }),
};

export const dashboardApi = {
  summary: () => api.get<T.DashboardSummary>('/api/dashboard/summary'),
  trends: (days: number) => api.get<T.DailyTrend[]>('/api/dashboard/query-trends', { days }),
  topTables: (days: number) => api.get<T.NamedCount[]>('/api/dashboard/top-tables', { days, top: 10 }),
  performance: (days: number) => api.get<T.ExecutionPerformance>('/api/dashboard/execution-performance', { days }),
  topUsers: (days: number) => api.get<T.NamedCount[]>('/api/dashboard/top-users', { days, top: 10 }),
  categories: () => api.get<T.NamedCount[]>('/api/dashboard/categories'),
  activity: () => api.get<T.RecentActivity>('/api/dashboard/recent-activity'),
};

export const usersApi = {
  list: (params: { page: number; pageSize: number; search?: string; role?: string }) => api.get<T.PagedResult<T.UserListItem>>('/api/users', params),
  create: (body: { fullName: string; email: string; password: string; roles: string[] }) => api.post<T.UserListItem>('/api/users', body),
  update: (id: number, body: { fullName: string; isActive: boolean; roles: string[] }) => api.put<T.UserListItem>(`/api/users/${id}`, body),
  resetPassword: (id: number, newPassword: string) => api.post<object>(`/api/users/${id}/reset-password`, { newPassword }),
  roles: () => api.get<T.Role[]>('/api/roles'),
  permissions: () => api.get<T.Permission[]>('/api/roles/permissions'),
  updateRolePermissions: (roleId: number, permissions: string[]) => api.put<T.Role>(`/api/roles/${roleId}/permissions`, { permissions }),
};

export const settingsApi = {
  list: () => api.get<T.AiProviderConfiguration[]>('/api/settings/ai'),
  create: (body: T.SaveAiProviderConfiguration) => api.post<T.AiProviderConfiguration>('/api/settings/ai', body),
  update: (id: number, body: T.SaveAiProviderConfiguration) => api.put<T.AiProviderConfiguration>(`/api/settings/ai/${id}`, body),
  remove: (id: number) => api.delete<object>(`/api/settings/ai/${id}`),
  activate: (id: number) => api.post<T.AiProviderConfiguration>(`/api/settings/ai/${id}/activate`),
  test: () => api.post<{ success: boolean; message: string; model?: string | null; latencyMs?: number | null }>('/api/settings/ai/test'),
  auditLogs: (params: { page: number; pageSize: number; action?: string; search?: string }) =>
    api.get<T.PagedResult<T.AuditLog>>('/api/audit-logs', params),
};
