using AIQueryGenerator.Application.Schema;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Security;

namespace AIQueryGenerator.UnitTests.Application;

public class AccessRuleTests
{
    private static AllowedDatabaseObject Rule(DbObjectType type, string schema, string? obj, string? col, ObjectAccessLevel level) =>
        new() { ObjectType = type, SchemaName = schema, ObjectName = obj, ColumnName = col, AccessLevel = level };

    [Fact]
    public void Nothing_is_allowed_without_rules() =>
        Assert.False(new AccessRuleEvaluator([]).IsTableAllowed("dbo", "Orders"));

    [Fact]
    public void Schema_allow_with_table_and_column_deny()
    {
        var e = new AccessRuleEvaluator(
        [
            Rule(DbObjectType.Schema, "dbo", null, null, ObjectAccessLevel.Allow),
            Rule(DbObjectType.Table, "dbo", "Secrets", null, ObjectAccessLevel.Deny),
            Rule(DbObjectType.Column, "dbo", "Customers", "Email", ObjectAccessLevel.Deny),
        ]);
        Assert.True(e.IsTableAllowed("dbo", "Orders"));
        Assert.True(e.IsTableAllowed("DBO", "orders"));
        Assert.False(e.IsTableAllowed("dbo", "Secrets"));
        Assert.False(e.IsTableAllowed("sales", "Orders"));
        Assert.True(e.IsColumnAllowed("dbo", "Customers", "CustomerName"));
        Assert.False(e.IsColumnAllowed("dbo", "Customers", "email"));
    }

    [Fact]
    public void Individual_table_allow_and_schema_deny_wins()
    {
        var e = new AccessRuleEvaluator(
        [
            Rule(DbObjectType.Table, "sales", "Orders", null, ObjectAccessLevel.Allow),
            Rule(DbObjectType.Schema, "hr", null, null, ObjectAccessLevel.Deny),
            Rule(DbObjectType.View, "hr", "Salaries", null, ObjectAccessLevel.Allow),
        ]);
        Assert.True(e.IsTableAllowed("sales", "Orders"));
        Assert.False(e.IsTableAllowed("sales", "Customers"));
        Assert.False(e.IsTableAllowed("hr", "Salaries"));
    }

    [Fact]
    public void Permission_matrix_matches_role_definitions()
    {
        var m = Permissions.DefaultRolePermissions;
        Assert.Equal(Permissions.Descriptions.Count, m[Roles.SuperAdmin].Length);
        Assert.DoesNotContain(Permissions.AdminsManage, m[Roles.Admin]);
        Assert.Contains(Permissions.ConnectionsManage, m[Roles.Admin]);
        Assert.Contains(Permissions.QueryEditSql, m[Roles.Developer]);
        Assert.DoesNotContain(Permissions.QueryEditSql, m[Roles.Analyst]);
        Assert.Contains(Permissions.ResultsExport, m[Roles.Analyst]);
        Assert.DoesNotContain(Permissions.QueryGenerate, m[Roles.Viewer]);
        Assert.DoesNotContain(Permissions.QueryExecute, m[Roles.Viewer]);
        Assert.DoesNotContain(Permissions.ConnectionsManage, m[Roles.Viewer]);
        Assert.Contains(Permissions.SharedReportsView, m[Roles.Viewer]);
    }
}
