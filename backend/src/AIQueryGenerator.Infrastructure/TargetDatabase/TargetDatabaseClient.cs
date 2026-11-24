using System.Data;
using System.Data.Common;
using System.Diagnostics;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Application.Sql;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Shared.Exceptions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Infrastructure.TargetDatabase;

/// <summary>
/// Executes already-validated, parameterised SELECT statements against a target database. Every execution runs in a
/// transaction that is always rolled back, reads at most MaxRows+1 rows, and honours timeouts and cancellation.
/// </summary>
public sealed class TargetDatabaseClient(ISqlConnectionFactory factory, ILogger<TargetDatabaseClient> logger) : ITargetDatabaseClient
{
    private const string PrivilegeProbe = """
        SELECT
            CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ServerVersion,
            SUSER_SNAME() AS LoginName,
            ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0) AS IsSysAdmin,
            ISNULL(IS_SRVROLEMEMBER('serveradmin'), 0) AS IsServerAdmin,
            ISNULL(IS_SRVROLEMEMBER('securityadmin'), 0) AS IsSecurityAdmin,
            ISNULL(IS_ROLEMEMBER('db_owner'), 0) AS IsDbOwner,
            ISNULL(IS_ROLEMEMBER('db_ddladmin'), 0) AS IsDdlAdmin,
            ISNULL(IS_ROLEMEMBER('db_datawriter'), 0) AS IsDataWriter,
            ISNULL(IS_ROLEMEMBER('db_securityadmin'), 0) AS IsDbSecurityAdmin,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL'), 0) AS CanControl,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER'), 0) AS CanAlter,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT'), 0) AS CanInsert,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE'), 0) AS CanUpdate,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE'), 0) AS CanDelete,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), 0) AS CanCreateTable,
            ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'EXECUTE'), 0) AS CanExecute,
            CASE WHEN EXISTS (
                SELECT 1 FROM sys.objects o
                WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
                  AND (HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT', 'INSERT') = 1
                    OR HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT', 'UPDATE') = 1
                    OR HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT', 'DELETE') = 1
                    OR HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(o.schema_id)) + '.' + QUOTENAME(o.name), 'OBJECT', 'ALTER') = 1)
            ) THEN 1 ELSE 0 END AS HasObjectWritePermission
        """;

    public async Task<ConnectionTestResult> TestConnectionAsync(DatabaseConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var sql = factory.Create(connection);
            await sql.OpenAsync(cancellationToken);
            var p = await sql.QuerySingleAsync<PrivilegeRow>(new CommandDefinition(PrivilegeProbe, commandTimeout: 30, cancellationToken: cancellationToken));

            var blocking = new List<string>();
            if (p.IsSysAdmin) blocking.Add("The login is a member of the sysadmin server role.");
            if (p.IsServerAdmin) blocking.Add("The login is a member of the serveradmin server role.");
            if (p.IsSecurityAdmin) blocking.Add("The login is a member of the securityadmin server role.");
            if (p.IsDbOwner) blocking.Add("The user is a member of db_owner.");
            if (p.IsDdlAdmin) blocking.Add("The user is a member of db_ddladmin.");
            if (p.IsDataWriter) blocking.Add("The user is a member of db_datawriter.");
            if (p.IsDbSecurityAdmin) blocking.Add("The user is a member of db_securityadmin.");
            if (p.CanControl) blocking.Add("The user has CONTROL on the database.");
            if (p.CanAlter) blocking.Add("The user has ALTER on the database.");
            if (p.CanInsert || p.CanUpdate || p.CanDelete) blocking.Add("The user has database-wide INSERT/UPDATE/DELETE permission.");
            if (p.CanCreateTable) blocking.Add("The user has CREATE TABLE permission.");
            if (p.HasObjectWritePermission) blocking.Add("The user can modify at least one table or view.");

            var warnings = new List<string>(blocking);
            if (p.CanExecute) warnings.Add("Note: the user has database-wide EXECUTE permission. Stored procedure execution is blocked by the validator, but revoking it is recommended.");

            var leastPrivilege = blocking.Count == 0;
            var message = leastPrivilege
                ? "Connection succeeded. The database principal is read-only."
                : "Connection succeeded, but the database principal has elevated permissions. Use a dedicated SELECT-only login before activating this connection.";
            return new ConnectionTestResult(true, leastPrivilege, message, p.ServerVersion, p.LoginName, warnings);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Connection test failed for connection {ConnectionId} (error {Number})", connection.ConnectionId, ex.Number);
            var mapped = SqlErrorMapper.Map(ex, 30);
            return new ConnectionTestResult(false, false, mapped.ErrorCode == ErrorCodes.SqlExecutionFailed ? "Connection failed." : mapped.Message, null, null, []);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbException)
        {
            logger.LogWarning(ex, "Connection test failed for connection {ConnectionId}", connection.ConnectionId);
            return new ConnectionTestResult(false, false, "Connection failed. Verify the server name, database and authentication settings.", null, null, []);
        }
    }

    public async Task<QueryResultData> ExecuteReadOnlyAsync(QueryExecutionRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var sql = factory.Create(request.Connection);
            await sql.OpenAsync(cancellationToken);
            await ApplySessionSettingsAsync(sql, request.CommandTimeoutSeconds, cancellationToken);

            await using var tx = (SqlTransaction)await sql.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await using var command = CreateCommand(sql, tx, request);

            var columns = new List<ResultColumn>();
            var rows = new List<object?[]>();
            var truncated = false;

            try
            {
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken))
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    columns.Add(new ResultColumn(string.IsNullOrEmpty(name) ? $"Column{i + 1}" : name, reader.GetDataTypeName(i), reader.GetFieldType(i).Name));
                }

                while (rows.Count < request.MaxRows && await reader.ReadAsync(cancellationToken))
                    rows.Add(ReadRow(reader, columns, request.MaxCellLength));

                if (rows.Count == request.MaxRows && await reader.ReadAsync(cancellationToken))
                {
                    truncated = true;
                    command.Cancel(); // stop the server streaming remaining rows
                }
            }
            }
            catch (SqlException) when (truncated)
            {
                // Expected: closing a reader after Cancel() reports the cancellation. All needed rows were read.
            }

            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException) { /* connection reset rolls back */ }
            return new QueryResultData { Columns = columns, Rows = rows, IsTruncated = truncated, DurationMs = (int)sw.ElapsedMilliseconds };
        }
        catch (SqlException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Query execution failed on connection {ConnectionId} (error {Number}, class {Class})", request.Connection.ConnectionId, ex.Number, ex.Class);
            throw SqlErrorMapper.Map(ex, request.CommandTimeoutSeconds);
        }
        catch (SqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    public async Task<string> GetEstimatedPlanXmlAsync(QueryExecutionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var sql = factory.Create(request.Connection);
            await sql.OpenAsync(cancellationToken);
            await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", sql)) await on.ExecuteNonQueryAsync(cancellationToken);

            string? plan;
            await using (var command = CreateCommand(sql, null, request))
                plan = (await command.ExecuteScalarAsync(cancellationToken)) as string;

            await using (var off = new SqlCommand("SET SHOWPLAN_XML OFF;", sql)) await off.ExecuteNonQueryAsync(cancellationToken);
            return plan ?? throw new ExternalServiceException("SQL Server did not return an execution plan.", ErrorCodes.SqlExecutionFailed, 422);
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Estimated plan failed on connection {ConnectionId} (error {Number})", request.Connection.ConnectionId, ex.Number);
            if (ex.Number == 262)
                throw new ExternalServiceException("The read-only database login needs the SHOWPLAN permission to display execution plans.", ErrorCodes.SqlPermissionDenied, 403, ex);
            throw SqlErrorMapper.Map(ex, request.CommandTimeoutSeconds);
        }
    }

    private static async Task ApplySessionSettingsAsync(SqlConnection sql, int timeoutSeconds, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"SET NOCOUNT ON; SET XACT_ABORT ON; SET ARITHABORT ON; SET LOCK_TIMEOUT {Math.Clamp(timeoutSeconds, 1, 600) * 1000};", sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SqlCommand CreateCommand(SqlConnection sql, SqlTransaction? tx, QueryExecutionRequest request)
    {
        var command = new SqlCommand(request.Sql, sql, tx)
        {
            CommandType = CommandType.Text,
            CommandTimeout = request.CommandTimeoutSeconds,
        };
        foreach (var p in request.Parameters)
            command.Parameters.Add(ToSqlParameter(p));
        return command;
    }

    private static SqlParameter ToSqlParameter(BoundParameter p)
    {
        var parameter = new SqlParameter("@" + p.Name, p.Type switch
        {
            SqlParameterType.String => SqlDbType.NVarChar,
            SqlParameterType.Int => SqlDbType.Int,
            SqlParameterType.BigInt => SqlDbType.BigInt,
            SqlParameterType.Decimal => SqlDbType.Decimal,
            SqlParameterType.Bool => SqlDbType.Bit,
            SqlParameterType.Date => SqlDbType.Date,
            SqlParameterType.DateTime => SqlDbType.DateTime2,
            SqlParameterType.Guid => SqlDbType.UniqueIdentifier,
            _ => SqlDbType.NVarChar,
        })
        {
            Value = p.Value ?? DBNull.Value,
        };
        if (p.Type == SqlParameterType.String) parameter.Size = p.Value is string s && s.Length > 4000 ? -1 : 4000;
        if (p.Type == SqlParameterType.Decimal) { parameter.Precision = 38; parameter.Scale = 10; }
        return parameter;
    }

    private static object?[] ReadRow(SqlDataReader reader, List<ResultColumn> columns, int maxCellLength)
    {
        var values = new object?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i)) continue;
            try
            {
                values[i] = ValueConverter.Convert(reader.GetValue(i), columns[i].DataType, maxCellLength);
            }
            catch (Exception ex) when (ex is InvalidCastException or FileNotFoundException or NotSupportedException)
            {
                values[i] = "(unsupported type)";
            }
        }
        return values;
    }

    private sealed class PrivilegeRow
    {
        public string? ServerVersion { get; init; }
        public string? LoginName { get; init; }
        public bool IsSysAdmin { get; init; }
        public bool IsServerAdmin { get; init; }
        public bool IsSecurityAdmin { get; init; }
        public bool IsDbOwner { get; init; }
        public bool IsDdlAdmin { get; init; }
        public bool IsDataWriter { get; init; }
        public bool IsDbSecurityAdmin { get; init; }
        public bool CanControl { get; init; }
        public bool CanAlter { get; init; }
        public bool CanInsert { get; init; }
        public bool CanUpdate { get; init; }
        public bool CanDelete { get; init; }
        public bool CanCreateTable { get; init; }
        public bool CanExecute { get; init; }
        public bool HasObjectWritePermission { get; init; }
    }
}
