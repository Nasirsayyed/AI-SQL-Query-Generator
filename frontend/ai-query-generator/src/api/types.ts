// DTOs mirroring the ASP.NET Core API contracts (enums are serialised as strings).

export interface ApiErrorItem {
  field: string;
  message: string;
}

export interface ApiEnvelope<T> {
  success: boolean;
  data: T;
  message?: string | null;
  errorCode?: string | null;
  errors: ApiErrorItem[];
  traceId?: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface UserProfile {
  userId: number;
  fullName: string;
  email: string;
  roles: string[];
  permissions: string[];
  lastLoginAt?: string | null;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: UserProfile;
}

export type SqlAuthenticationType = 'SqlPassword' | 'WindowsIntegrated' | 'EntraManagedIdentity' | 'EntraDefault';
export type ConnectionStatus = 'Untested' | 'Healthy' | 'Failed' | 'Unsafe';
export type DbObjectType = 'Schema' | 'Table' | 'View' | 'Column';
export type ObjectAccessLevel = 'Allow' | 'Deny';
export type QueryExecutionStatus =
  | 'Generated'
  | 'ClarificationRequested'
  | 'ValidationFailed'
  | 'Succeeded'
  | 'Failed'
  | 'TimedOut'
  | 'Cancelled';

export interface ConnectionSummary {
  connectionId: number;
  connectionName: string;
  databaseName: string;
  isActive: boolean;
  status: ConnectionStatus;
  lastSchemaRefreshAt?: string | null;
}

export interface Connection {
  connectionId: number;
  connectionName: string;
  serverName: string;
  databaseName: string;
  authenticationType: SqlAuthenticationType;
  username?: string | null;
  hasStoredCredentials: boolean;
  encryptConnection: boolean;
  trustServerCertificate: boolean;
  connectionTimeoutSeconds: number;
  commandTimeoutSeconds: number;
  maxRows: number;
  isActive: boolean;
  status: ConnectionStatus;
  lastTestedAt?: string | null;
  lastTestMessage?: string | null;
  lastSchemaRefreshAt?: string | null;
  allowedRoles: string[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveConnectionRequest {
  connectionName: string;
  serverName: string;
  databaseName: string;
  authenticationType: SqlAuthenticationType;
  username?: string | null;
  password?: string | null;
  encryptConnection: boolean;
  trustServerCertificate: boolean;
  connectionTimeoutSeconds: number;
  commandTimeoutSeconds: number;
  maxRows: number;
  allowedRoles: string[];
}

export interface ConnectionTestResult {
  success: boolean;
  isLeastPrivilege: boolean;
  message: string;
  serverVersion?: string | null;
  loginName?: string | null;
  privilegeWarnings: string[];
  testedAt: string;
}

export interface SchemaRefreshResult {
  schemas: number;
  tables: number;
  views: number;
  columns: number;
  relationships: number;
  tablesAdded: number;
  tablesUpdated: number;
  tablesRemoved: number;
  tablesUnchanged: number;
  fullRefresh: boolean;
  refreshedAt: string;
  durationMs: number;
}

export interface AllowedObjectRule {
  objectType: DbObjectType;
  schemaName: string;
  objectName?: string | null;
  columnName?: string | null;
  accessLevel: ObjectAccessLevel;
}

export interface TableSummary {
  tableId: number;
  schemaName: string;
  tableName: string;
  objectType: DbObjectType;
  isAllowed: boolean;
  columnCount: number;
  allowedColumnCount: number;
  rowCountEstimate?: number | null;
  description?: string | null;
}

export interface SchemaNode {
  schemaName: string;
  isAllowed: boolean;
  tables: TableSummary[];
}

export interface Relationship {
  name: string;
  fromTable: string;
  fromColumn: string;
  toTable: string;
  toColumn: string;
}

export interface SchemaOverview {
  connectionId: number;
  databaseName: string;
  lastSchemaRefreshAt?: string | null;
  canConfigure: boolean;
  schemas: SchemaNode[];
  relationships: Relationship[];
}

export interface ColumnInfo {
  columnName: string;
  dataType: string;
  isNullable: boolean;
  isPrimaryKey: boolean;
  isIdentity: boolean;
  isComputed: boolean;
  isAllowed: boolean;
  ordinalPosition: number;
  description?: string | null;
  references?: string | null;
}

export interface IndexInfo {
  indexName: string;
  isUnique: boolean;
  isPrimaryKey: boolean;
  columns: string;
}

export interface TableDetail {
  tableId: number;
  schemaName: string;
  tableName: string;
  objectType: DbObjectType;
  isAllowed: boolean;
  rowCountEstimate?: number | null;
  description?: string | null;
  columns: ColumnInfo[];
  indexes: IndexInfo[];
  relationships: Relationship[];
  lastSyncedAt: string;
}

export interface SchemaSearchResult {
  kind: string;
  schemaName: string;
  tableName: string;
  columnName?: string | null;
  dataType?: string | null;
  isAllowed: boolean;
}

export interface CompletionTable {
  schemaName: string;
  tableName: string;
  objectType: DbObjectType;
  columns: { name: string; dataType: string }[];
}

export type SqlParameterType = 'String' | 'Int' | 'BigInt' | 'Decimal' | 'Bool' | 'Date' | 'DateTime' | 'Guid';

export interface QueryParameter {
  name: string;
  type: SqlParameterType;
  value: string | null;
  description?: string | null;
}

export interface SqlValidationIssue {
  code: string;
  message: string;
  line?: number | null;
  column?: number | null;
}

export interface SqlValidationResponse {
  isValid: boolean;
  message: string;
  errors: SqlValidationIssue[];
  warnings: SqlValidationIssue[];
  referencedObjects: string[];
  referencedParameters: string[];
  validationToken?: string | null;
  validatedAt: string;
}

export interface ResultColumn {
  name: string;
  dataType: string;
  clrType: string;
}

export type CellValue = string | number | boolean | null;

export interface QueryResult {
  queryHistoryId?: number | null;
  columns: ResultColumn[];
  rows: CellValue[][];
  rowCount: number;
  isTruncated: boolean;
  maxRows: number;
  durationMs: number;
  executedAt: string;
  connectionId: number;
  connectionName: string;
  warnings: string[];
}

export type ChartType = 'bar' | 'horizontalBar' | 'line' | 'area' | 'pie' | 'doughnut' | 'scatter';
export type Aggregation = 'none' | 'sum' | 'avg' | 'count' | 'min' | 'max';

export interface ChartSuggestion {
  recommended: boolean;
  chartType?: ChartType | null;
  xAxis?: string | null;
  yAxis?: string | null;
  aggregation?: Aggregation | null;
  title?: string | null;
  reason?: string | null;
  source: string;
}

export interface GenerateSqlResponse {
  success: boolean;
  needsClarification: boolean;
  clarificationQuestion?: string | null;
  sqlQuery?: string | null;
  queryTitle: string;
  explanation: string;
  tablesUsed: string[];
  columnsUsed: string[];
  parameters: QueryParameter[];
  warnings: string[];
  chartSuggestion?: ChartSuggestion | null;
  validation?: SqlValidationResponse | null;
  queryHistoryId?: number | null;
  model: string;
  promptVersion: string;
}

export interface ExplainSqlResponse {
  summary: string;
  steps: { clause: string; description: string }[];
  beginnerExplanation?: string | null;
  notes: string[];
  model: string;
  promptVersion: string;
}

export interface OptimizeSqlResponse {
  summary: string;
  suggestions: { title: string; description: string; severity: 'info' | 'warning' | 'critical' }[];
  optimizedSql?: string | null;
  optimizedSqlValidation?: SqlValidationResponse | null;
  model: string;
  promptVersion: string;
}

export interface ExecutionPlanSummary {
  estimatedTotalCost: number;
  estimatedRows: number;
  topOperators: { physicalOp: string; logicalOp: string; estimatedRows: number; estimatedSubtreeCost: number; objectName?: string | null }[];
  warnings: string[];
  missingIndexes: string[];
}

export interface QueryHistoryItem {
  queryId: number;
  queryTitle: string;
  userQuestion?: string | null;
  generatedSql?: string | null;
  executedSql?: string | null;
  connectionId: number;
  connectionName: string;
  userId: number;
  userName: string;
  executionDurationMs?: number | null;
  returnedRows?: number | null;
  executionStatus: QueryExecutionStatus;
  hasParameters: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface QueryHistoryDetail {
  item: QueryHistoryItem;
  explanation?: string | null;
  tablesUsed: string[];
  chartSuggestion?: ChartSuggestion | null;
  parameters: QueryParameter[];
  promptVersion?: string | null;
  aiModel?: string | null;
  errorMessage?: string | null;
  isOwner: boolean;
}

export interface SavedQuery {
  savedQueryId: number;
  queryTitle: string;
  description?: string | null;
  userQuestion?: string | null;
  sqlQuery: string;
  category?: string | null;
  isShared: boolean;
  isFavorite: boolean;
  isOwner: boolean;
  ownerName: string;
  connectionId: number;
  connectionName: string;
  parameters: QueryParameter[];
  chartConfigJson?: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveQueryRequest {
  connectionId: number;
  queryTitle: string;
  description?: string | null;
  userQuestion?: string | null;
  sqlQuery: string;
  category?: string | null;
  isShared: boolean;
  parameters: QueryParameter[];
  chartConfigJson?: string | null;
  sourceQueryHistoryId?: number | null;
}

export interface DashboardSummary {
  totalQueries: number;
  successfulQueries: number;
  failedQueries: number;
  averageExecutionTimeMs: number;
  queriesGeneratedToday: number;
  activeConnections: number;
  totalSavedQueries: number;
  isGlobalScope: boolean;
}

export interface DailyTrend {
  date: string;
  total: number;
  succeeded: number;
  failed: number;
  generated: number;
}

export interface NamedCount {
  name: string;
  count: number;
}

export interface ExecutionPerformance {
  daily: { date: string; averageMs: number; maxMs: number; executions: number }[];
  byConnection: { connectionName: string; averageMs: number; executions: number }[];
}

export interface ActivityItem {
  id: number;
  title: string;
  userName?: string | null;
  connectionName?: string | null;
  status: string;
  durationMs?: number | null;
  detail?: string | null;
  at: string;
}

export interface RecentActivity {
  latestQueries: ActivityItem[];
  latestFailures: ActivityItem[];
  metadataRefreshes: ActivityItem[];
}

export interface UserListItem {
  userId: number;
  fullName: string;
  email: string;
  isActive: boolean;
  roles: string[];
  lastLoginAt?: string | null;
  createdAt: string;
}

export interface Role {
  roleId: number;
  name: string;
  description?: string | null;
  permissions: string[];
  userCount: number;
}

export interface Permission {
  code: string;
  description?: string | null;
}

export type AiProviderType = 'OpenAI' | 'AzureOpenAI' | 'OpenAICompatible';

export interface AiProviderConfiguration {
  aiProviderConfigurationId: number;
  name: string;
  providerType: AiProviderType;
  baseUrl: string;
  model: string;
  apiKeySecretReference?: string | null;
  hasApiKey: boolean;
  temperature: number;
  maxOutputTokens: number;
  timeoutSeconds: number;
  maxSchemaTables: number;
  isActive: boolean;
  createdAt: string;
  updatedAt?: string | null;
}

export interface SaveAiProviderConfiguration {
  name: string;
  providerType: AiProviderType;
  baseUrl: string;
  model: string;
  apiKey?: string | null;
  temperature: number;
  maxOutputTokens: number;
  timeoutSeconds: number;
  maxSchemaTables: number;
}

export interface AiStatus {
  configured: boolean;
  providerName?: string | null;
  model?: string | null;
}

export interface AuditLog {
  auditLogId: number;
  userId?: number | null;
  userName?: string | null;
  action: string;
  entityType?: string | null;
  entityId?: string | null;
  details?: string | null;
  ipAddress?: string | null;
  createdAt: string;
}
