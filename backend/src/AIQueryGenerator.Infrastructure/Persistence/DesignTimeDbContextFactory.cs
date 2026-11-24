using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIQueryGenerator.Infrastructure.Persistence;

/// <summary>Used by "dotnet ef" to create migrations without starting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ApplicationDb")
            ?? "Server=localhost;Database=AIQueryGeneratorApp;Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .Options;
        return new ApplicationDbContext(options, TimeProvider.System);
    }
}
