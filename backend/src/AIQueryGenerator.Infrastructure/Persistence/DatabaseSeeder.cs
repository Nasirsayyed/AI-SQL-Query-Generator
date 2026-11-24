using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Domain.Entities;
using AIQueryGenerator.Domain.Enums;
using AIQueryGenerator.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIQueryGenerator.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public SuperAdminSeed? SuperAdmin { get; set; }
    public DemoConnectionSeed? DemoConnection { get; set; }

    public sealed class SuperAdminSeed
    {
        public string? FullName { get; set; } = "Super Administrator";
        public string? Email { get; set; }

        /// <summary>Supply via environment variable Seed__SuperAdmin__Password; never commit it.</summary>
        public string? Password { get; set; }
    }

    public sealed class DemoConnectionSeed
    {
        public bool Enabled { get; set; }
        public string ConnectionName { get; set; } = "AIQueryDemoDB (sample)";
        public string ServerName { get; set; } = "localhost";
        public string DatabaseName { get; set; } = "AIQueryDemoDB";
        public string Username { get; set; } = "aiqg_reader";

        /// <summary>Secret reference, e.g. "env:AIQG_SECRET_DEMO_DB_PASSWORD".</summary>
        public string? PasswordReference { get; set; }
        public bool TrustServerCertificate { get; set; } = true;
    }
}

/// <summary>Idempotently seeds roles, permissions and (optionally) the first Super Admin and the demo connection.</summary>
public sealed class DatabaseSeeder(ApplicationDbContext db, IPasswordHasher hasher, IConfiguration configuration, ILogger<DatabaseSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var options = configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();

        var permissions = await db.Permissions.ToListAsync(ct);
        foreach (var (code, description) in Permissions.Descriptions)
        {
            var existing = permissions.FirstOrDefault(p => p.Code == code);
            if (existing is null)
            {
                existing = new Permission { Code = code, Description = description };
                db.Permissions.Add(existing);
                permissions.Add(existing);
            }
            else existing.Description = description;
        }

        var roles = await db.Roles.Include(r => r.RolePermissions).ToListAsync(ct);
        foreach (var roleName in Roles.All)
        {
            var role = roles.FirstOrDefault(r => r.Name == roleName);
            if (role is not null)
            {
                // Super Admin always holds every permission (including ones added in newer versions).
                if (roleName == Roles.SuperAdmin)
                    foreach (var p in permissions.Where(p => role.RolePermissions.All(rp => rp.Permission?.Code != p.Code && rp.PermissionId != p.PermissionId)))
                        role.RolePermissions.Add(new RolePermission { Permission = p });
                continue;
            }

            role = new Role { Name = roleName, Description = DescribeRole(roleName), IsSystem = true };
            foreach (var code in Permissions.DefaultRolePermissions[roleName])
                role.RolePermissions.Add(new RolePermission { Permission = permissions.First(p => p.Code == code) });
            db.Roles.Add(role);
            roles.Add(role);
        }
        await db.SaveChangesAsync(ct);

        await SeedSuperAdminAsync(options.SuperAdmin, roles, ct);
        await SeedDemoConnectionAsync(options.DemoConnection, roles, ct);
    }

    private async Task SeedSuperAdminAsync(SeedOptions.SuperAdminSeed? seed, List<Role> roles, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;
        if (string.IsNullOrWhiteSpace(seed?.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            logger.LogWarning("No users exist and Seed:SuperAdmin is not configured. Set Seed__SuperAdmin__Email and Seed__SuperAdmin__Password to create the first Super Admin.");
            return;
        }

        var user = new User
        {
            FullName = seed.FullName ?? "Super Administrator",
            Email = seed.Email.Trim(),
            NormalizedEmail = seed.Email.Trim().ToUpperInvariant(),
            PasswordHash = hasher.Hash(seed.Password),
            IsActive = true,
        };
        user.UserRoles.Add(new UserRole { Role = roles.First(r => r.Name == Roles.SuperAdmin), AssignedAt = DateTime.UtcNow });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created initial Super Admin {Email}", user.Email);
    }

    private async Task SeedDemoConnectionAsync(SeedOptions.DemoConnectionSeed? seed, List<Role> roles, CancellationToken ct)
    {
        if (seed is not { Enabled: true } || string.IsNullOrWhiteSpace(seed.PasswordReference)) return;
        if (await db.DatabaseConnections.AnyAsync(c => c.ConnectionName == seed.ConnectionName && !c.IsDeleted, ct)) return;

        var connection = new DatabaseConnection
        {
            ConnectionName = seed.ConnectionName,
            ServerName = seed.ServerName,
            DatabaseName = seed.DatabaseName,
            AuthenticationType = SqlAuthenticationType.SqlPassword,
            Username = seed.Username,
            EncryptedCredentialsReference = seed.PasswordReference,
            EncryptConnection = true,
            TrustServerCertificate = seed.TrustServerCertificate,
        };
        foreach (var role in roles)
            connection.Permissions.Add(new DatabaseConnectionPermission { RoleId = role.RoleId, CanQuery = true, CreatedAt = DateTime.UtcNow });

        // Demo policy: the whole dbo schema is available to AI, but customer e-mail addresses are restricted.
        connection.AllowedObjects.Add(new AllowedDatabaseObject { ObjectType = DbObjectType.Schema, SchemaName = "dbo", AccessLevel = ObjectAccessLevel.Allow, CreatedAt = DateTime.UtcNow });
        connection.AllowedObjects.Add(new AllowedDatabaseObject { ObjectType = DbObjectType.Column, SchemaName = "dbo", ObjectName = "Customers", ColumnName = "Email", AccessLevel = ObjectAccessLevel.Deny, CreatedAt = DateTime.UtcNow });

        db.DatabaseConnections.Add(connection);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo connection {Name}; test and activate it from the Connections screen.", seed.ConnectionName);
    }

    private static string DescribeRole(string role) => role switch
    {
        Roles.SuperAdmin => "Full application access, including administrator management.",
        Roles.Admin => "Manages users, roles, database connections, schema permissions and AI settings.",
        Roles.Developer => "Generates, edits and executes permitted read-only SQL.",
        Roles.Analyst => "Generates and executes permitted queries, views charts and exports results.",
        Roles.Viewer => "Views shared reports and permitted results.",
        _ => role,
    };
}
